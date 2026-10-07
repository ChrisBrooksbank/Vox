using Vox.Core.Text;

namespace Vox.Core.Accessibility;

/// <summary>
/// Text the review cursor can review besides the navigator object: the focused control's text
/// and the foreground window's. The Get methods must be called on the UIA thread.
/// </summary>
public interface IReviewTextSource
{
    /// <summary>The focused control's text document, or null when it has no text.</summary>
    ITextDocument? GetFocusedText();

    /// <summary>The foreground window's handle (any thread).</summary>
    IntPtr ForegroundWindow { get; }

    /// <summary>All the text of <paramref name="window"/>, or null.</summary>
    ITextDocument? GetWindowText(IntPtr window);
}

/// <summary>Review text from UIA: the focused text source, and a capture of the foreground window.</summary>
public sealed class UIAReviewTextSource(UIAProvider provider, IFocusedTextSource focusedText, IForegroundWindow foregroundWindow)
    : IReviewTextSource
{
    private const int MaxWindowItems = 2000;

    public ITextDocument? GetFocusedText() => focusedText.GetFocusedDocument();

    public IntPtr ForegroundWindow
    {
        get
        {
            try { return foregroundWindow.Get()?.Handle ?? IntPtr.Zero; }
            catch { return IntPtr.Zero; }
        }
    }

    public ITextDocument? GetWindowText(IntPtr window)
    {
        if (window == IntPtr.Zero)
            return null;
        var element = provider.Automation.ElementFromHandle(window);
        var cached = provider.WithDocumentCaptureTimeout(() => element.BuildUpdatedCache(provider.SubtreeCacheRequest));
        // One line per text or control, so the review cursor moves through them by line
        var items = Navigation.WhereAmI.WindowItems(UIAElementSnapshot.Capture(cached), MaxWindowItems);
        return new StringTextDocument(string.Join("\n", items));
    }
}
