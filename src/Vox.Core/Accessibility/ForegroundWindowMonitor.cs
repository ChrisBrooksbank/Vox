using System.Runtime.InteropServices;
using Vox.Core.Pipeline;

namespace Vox.Core.Accessibility;

/// <summary>The foreground window: its handle, title and process.</summary>
public readonly record struct ForegroundWindowInfo(IntPtr Handle, string Title, int ProcessId);

/// <summary>Reads the foreground window without UIA (so it works while UIA is busy).</summary>
public interface IForegroundWindow
{
    ForegroundWindowInfo? Get();
}

/// <summary>
/// <see cref="IForegroundWindow"/> through Win32. The title comes from InternalGetWindowText,
/// which never sends a message to the window, so a hung application can't block it.
/// </summary>
public sealed class Win32ForegroundWindow : IForegroundWindow
{
    public ForegroundWindowInfo? Get()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        var buffer = new char[512];
        int length = InternalGetWindowText(hwnd, buffer, buffer.Length);
        return new ForegroundWindowInfo(hwnd, new string(buffer, 0, Math.Max(0, length)), (int)pid);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int InternalGetWindowText(IntPtr hWnd, [Out] char[] text, int maxCount);
}

/// <summary>
/// Notices when focus has moved into another top-level window, so its title is said before the
/// focused control ("Save As, dialog ... File name, edit"), and posts
/// <see cref="ForegroundWindowChangedEvent"/>. Used as the pipeline's
/// <see cref="EventPipeline.FocusContextProvider"/>; called on the pipeline thread.
/// </summary>
public sealed class ForegroundWindowMonitor
{
    private readonly IForegroundWindow _foreground;
    private readonly IEventSink _eventSink;
    private IntPtr _lastWindow;

    public ForegroundWindowMonitor(IForegroundWindow foreground, IEventSink eventSink)
    {
        _foreground = foreground;
        _eventSink = eventSink;
    }

    /// <summary>
    /// For a focus change: the new foreground window's title if focus has moved into another
    /// window since the last call, else null. The title is left out when the focused control is
    /// the window itself (it would be said twice) or has no title.
    /// </summary>
    public string? FocusContext(FocusChangedEvent focus)
    {
        ForegroundWindowInfo? info;
        try { info = _foreground.Get(); }
        catch { return null; }
        if (info is not { } window || window.Handle == _lastWindow)
            return null;

        _lastWindow = window.Handle;
        _eventSink.Post(new ForegroundWindowChangedEvent(DateTimeOffset.UtcNow, window.Handle, window.Title, window.ProcessId));

        var title = window.Title.Trim();
        if (title.Length == 0 || string.Equals(title, focus.ElementName.Trim(), StringComparison.Ordinal))
            return null;
        return title;
    }
}
