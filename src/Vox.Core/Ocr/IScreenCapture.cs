namespace Vox.Core.Ocr;

/// <summary>Takes pictures of the screen (for text recognition). Safe to call from any thread.</summary>
public interface IScreenCapture
{
    /// <summary>
    /// Capturing is allowed: never in secure mode (the sign-in, lock and UAC screens), where
    /// whatever is on the screen belongs to no one at the keyboard.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// A picture of <paramref name="bounds"/> (clipped to the screen), or null when none of it is
    /// on the screen. Throws <see cref="InvalidOperationException"/> when not <see cref="IsAvailable"/>.
    /// </summary>
    CapturedImage? Capture(ScreenRect bounds);
}
