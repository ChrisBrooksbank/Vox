namespace Vox.Core.Braille;

/// <summary>
/// A braille display driver (HID braille, BRLTTY): finds a display, keeps it open and reports when
/// it goes away. <c>BrailleDisplayManager</c> asks each driver in turn to connect, and asks again
/// after <see cref="Disconnected"/> (a display unplugged, the BRLTTY server stopped).
/// </summary>
public interface IBrailleDisplayDriver : IBrailleDisplay, IDisposable
{
    /// <summary>The driver's id in the <c>BrailleDisplayDriver</c> setting ("HID", "BRLTTY").</summary>
    string Id { get; }

    /// <summary>
    /// Looks for a display and opens it. True when one is connected (also when it already was);
    /// false, without throwing, when none is found or it can't be opened. Never blocks for more
    /// than about a second.
    /// </summary>
    bool TryConnect();

    /// <summary>Raised (on the driver's thread) when a connected display is lost.</summary>
    event EventHandler? Disconnected;
}
