using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.Accessibility;

/// <summary>
/// Reads a dialog's message when focus moves into it (<see cref="DialogText"/>): the window's
/// title and focused control are said by the focus announcement; this queues the text after them.
/// A window is a dialog if it has the standard dialog class (#32770) or UIA says it is one.
/// </summary>
public sealed class DialogReader
{
    private const int UIA_IsDialogPropertyId = 30174;
    private const string DialogClassName = "#32770";

    private readonly UIAThread _uiaThread;
    private readonly UIAProvider _uiaProvider;
    private readonly SpeechQueue _speechQueue;
    private readonly ILogger<DialogReader> _logger;

    public DialogReader(UIAThread uiaThread, UIAProvider uiaProvider, SpeechQueue speechQueue, ILogger<DialogReader> logger)
    {
        _uiaThread = uiaThread;
        _uiaProvider = uiaProvider;
        _speechQueue = speechQueue;
        _logger = logger;
    }

    /// <summary>Focus moved into another window: if it is a dialog, read its message.</summary>
    public async Task HandleForegroundWindowChangedAsync(ForegroundWindowChangedEvent e)
    {
        if (e.WindowHandle == IntPtr.Zero || e.ProcessId == Environment.ProcessId)
            return;

        IReadOnlyList<string> texts;
        try
        {
            texts = await _uiaThread.RunAsync(() => ReadDialog(e.WindowHandle), UIAThread.DocumentTimeout).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the dialog");
            return;
        }

        if (texts.Count > 0)
            _speechQueue.Enqueue(new Utterance(string.Join(". ", texts), SpeechPriority.High));
    }

    /// <summary>The dialog's text, or nothing if the window isn't a dialog. UIA thread only.</summary>
    private IReadOnlyList<string> ReadDialog(IntPtr hwnd)
    {
        var automation = _uiaProvider.Automation;
        var window = automation.ElementFromHandle(hwnd);
        if (window is null)
            return [];

        bool isDialog = GetClassName(hwnd) == DialogClassName;
        if (!isDialog)
        {
            try { isDialog = window.GetCurrentPropertyValue(UIA_IsDialogPropertyId) is true; }
            catch { /* older Windows: no IsDialog property */ }
        }
        if (!isDialog)
            return [];

        var cached = _uiaProvider.WithDocumentCaptureTimeout(() => window.BuildUpdatedCache(_uiaProvider.SubtreeCacheRequest));
        return DialogText.Collect(UIAElementSnapshot.Capture(cached));
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var builder = new StringBuilder(64);
        return GetClassName(hwnd, builder, builder.Capacity) > 0 ? builder.ToString() : string.Empty;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);
}
