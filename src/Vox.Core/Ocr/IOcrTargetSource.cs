using Vox.Core.Buffer;

namespace Vox.Core.Ocr;

/// <summary>
/// Finds where on the screen to recognise: the navigator object, the foreground window, or an
/// element of the page being browsed. Results are null when the thing has no location (or none
/// on the screen). Safe to call from any thread; UIA work runs on the UIA thread.
/// </summary>
public interface IOcrTargetSource
{
    /// <summary>The navigator object's bounds.</summary>
    Task<ScreenRect?> NavigatorBoundsAsync();

    /// <summary>The foreground window's bounds.</summary>
    Task<ScreenRect?> WindowBoundsAsync();

    /// <summary>The bounds of the element behind <paramref name="node"/>, scrolled into view first.</summary>
    Task<ScreenRect?> ElementBoundsAsync(VBufferNode node);
}
