using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Accessibility;

namespace Vox.Core.AppModules.Office;

/// <summary>The Office applications whose object model Vox reads.</summary>
public enum OfficeApp
{
    Word,
    Excel,
    Outlook,
    PowerPoint,
}

/// <summary>
/// Calls into an Office application's object model (out-of-process COM, late bound). Office's UIA
/// leaves out much of what people need (formulas, comments, tracked changes, slide notes), which
/// its object model has.
/// </summary>
public interface IOfficeObjectModel
{
    /// <summary>
    /// Runs <paramref name="work"/> with the application's <c>Application</c> object (dynamic) on
    /// the Office thread. <paramref name="window"/> is the application's foreground window, used to
    /// find the instance that owns it (0: any running instance). Returns null when the application
    /// isn't running, is busy (a cell being edited, a modal dialog), took longer than
    /// <paramref name="timeout"/>, or <paramref name="work"/> threw.
    /// </summary>
    Task<T?> RunAsync<T>(OfficeApp app, IntPtr window, Func<dynamic, T?> work, TimeSpan? timeout = null) where T : class;
}

/// <summary>
/// <see cref="IOfficeObjectModel"/> on its own STA thread (Office's objects are apartment
/// threaded), with a timeout on every call: a call that doesn't finish in time is abandoned and the
/// thread replaced, so a hung Office never holds up the next call. The application object is found
/// through the window's native object model (<c>AccessibleObjectFromWindow</c> with
/// <c>OBJID_NATIVEOM</c> on the document window), which works as soon as the window exists, and
/// else through the running object table. Each Office thread caches it per application until a
/// call fails.
/// </summary>
public sealed class OfficeObjectModel : IOfficeObjectModel, IDisposable
{
    /// <summary>How long one call may take by default.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(1);

    private readonly UIAThread _thread;
    private readonly ILogger _logger;
    // Each Office thread's own objects: an object belongs to the apartment of the thread that
    // made it, and a call abandoned on a replaced thread may still be adding one when it returns
    private readonly ThreadLocal<Dictionary<OfficeApp, (object Application, IntPtr Window)>> _applications =
        new(() => new Dictionary<OfficeApp, (object Application, IntPtr Window)>());

    public OfficeObjectModel(ILogger<OfficeObjectModel>? logger = null, ILogger<UIAThread>? threadLogger = null)
    {
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _thread = new UIAThread(threadLogger ?? NullLogger<UIAThread>.Instance, "Vox-Office-STA");
        // The thread is stuck in a call into Office: later calls get a new thread, which finds
        // the applications again (the objects made on the old one stay with it)
        _thread.CallTimedOut += (_, _) => _thread.ReplaceStuckThread();
    }

    public async Task<T?> RunAsync<T>(OfficeApp app, IntPtr window, Func<dynamic, T?> work, TimeSpan? timeout = null) where T : class
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            return await _thread.RunAsync(() =>
            {
                var application = Application(app, window);
                if (application is null)
                    return null;
                try
                {
                    return work(application);
                }
                catch (COMException ex) when (IsDisconnected(ex))
                {
                    // Office was closed or restarted: find it again next time
                    _applications.Value!.Remove(app);
                    throw;
                }
            }, timeout ?? DefaultTimeout).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (Exception ex)
        {
            // Busy (RPC_E_CALL_REJECTED while a cell is edited), gone, timed out, or a member
            // this version of Office doesn't have
            _logger.LogDebug(ex, "{App} object model call failed", app);
            return null;
        }
    }

    private object? Application(OfficeApp app, IntPtr window)
    {
        var applications = _applications.Value!;
        if (applications.TryGetValue(app, out var cached) && (window == IntPtr.Zero || cached.Window == window))
            return cached.Application;

        var application = FromWindow(app, window) ?? FromRunningObjectTable(app);
        if (application is not null)
            applications[app] = (application, window);
        return application;
    }

    /// <summary>The document window's native object model: Word's and Excel's Window, PowerPoint's DocumentWindow.</summary>
    private static object? FromWindow(OfficeApp app, IntPtr topLevel)
    {
        if (topLevel == IntPtr.Zero || DocumentWindowClass(app) is not { } className)
            return null;
        var child = FindDescendant(topLevel, className);
        if (child == IntPtr.Zero)
            return null;
        var iid = IDispatchId;
        if (AccessibleObjectFromWindow(child, ObjIdNativeOm, ref iid, out var native) != 0 || native is null)
            return null;
        try
        {
            return ((dynamic)native).Application;
        }
        catch
        {
            return null;
        }
    }

    private static object? FromRunningObjectTable(OfficeApp app)
    {
        if (CLSIDFromProgID(ProgId(app), out var clsid) != 0)
            return null;
        return GetActiveObject(ref clsid, IntPtr.Zero, out var application) == 0 ? application : null;
    }

    /// <summary>The class of the window that holds a document, whose native object model leads to the application.</summary>
    public static string? DocumentWindowClass(OfficeApp app) => app switch
    {
        OfficeApp.Word => "_WwG",
        OfficeApp.Excel => "EXCEL7",
        OfficeApp.PowerPoint => "mdiClass",
        _ => null,
    };

    public static string ProgId(OfficeApp app) => app switch
    {
        OfficeApp.Word => "Word.Application",
        OfficeApp.Excel => "Excel.Application",
        OfficeApp.Outlook => "Outlook.Application",
        _ => "PowerPoint.Application",
    };

    // RPC_E_DISCONNECTED, RPC_S_SERVER_UNAVAILABLE, CO_E_OBJNOTCONNECTED
    private static bool IsDisconnected(COMException ex) =>
        ex.HResult is unchecked((int)0x80010108) or unchecked((int)0x800706BA) or unchecked((int)0x800401FD);

    private static IntPtr FindDescendant(IntPtr parent, string className)
    {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, (hwnd, _) =>
        {
            var buffer = new char[64];
            int length = GetClassName(hwnd, buffer, buffer.Length);
            if (length > 0 && new string(buffer, 0, length) == className)
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public void Dispose()
    {
        _thread.Dispose();
        _applications.Dispose();
    }

    private const uint ObjIdNativeOm = 0xFFFFFFF0;
    private static readonly Guid IDispatchId = new("00020400-0000-0000-C000-000000000046");

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, [Out] char[] className, int maxCount);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint objectId, ref Guid riid,
        [MarshalAs(UnmanagedType.IDispatch)] out object? ppvObject);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgID(string progId, out Guid clsid);

    [DllImport("oleaut32.dll")]
    private static extern int GetActiveObject(ref Guid clsid, IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object? application);
}
