namespace Vox.Core.Braille;

/// <summary>
/// Keys pressed together on a braille display, reported once the last of them is released.
/// <see cref="Keys"/> are the driver's own names for them ("dot1", "space", "panLeft", "routing"),
/// which the per-display key maps in <c>assets/braille/keymaps/</c> resolve to commands.
/// </summary>
/// <param name="Keys">The display-specific names of the keys in the chord, in the order pressed.</param>
/// <param name="RoutingCell">The cell of the routing key in the chord (0-based), or null when none was pressed.</param>
public sealed record BrailleKeyEventArgs(IReadOnlyList<string> Keys, int? RoutingCell = null);

/// <summary>
/// A refreshable braille display (HID braille, BRLTTY, or a fake in tests). Drivers raise
/// <see cref="KeyPressed"/> on their own thread; handlers post to the pipeline and return.
/// </summary>
public interface IBrailleDisplay
{
    /// <summary>The driver's name for the display, as shown in settings ("HID braille display").</summary>
    string Name { get; }

    /// <summary>True while a display is attached and accepting output.</summary>
    bool IsConnected { get; }

    /// <summary>The number of text cells (status cells not included); 0 while disconnected.</summary>
    int CellCount { get; }

    /// <summary>
    /// Shows <paramref name="cells"/> (see <see cref="BrailleDots"/>), padded with blank cells or cut to
    /// <see cref="CellCount"/>. <paramref name="cursorPosition"/> is the cell under the cursor, or null for
    /// none: the cells already show the cursor (Vox draws its shape and blink), so drivers only pass the
    /// position on where their protocol carries one and never draw it again. Ignored while disconnected.
    /// The caller may reuse the buffer after the call returns.
    /// </summary>
    void Write(ReadOnlyMemory<byte> cells, int? cursorPosition);

    /// <summary>Raised when keys on the display are pressed.</summary>
    event EventHandler<BrailleKeyEventArgs>? KeyPressed;
}

/// <summary>No braille display: nothing connected, writes go nowhere.</summary>
public sealed class NoBrailleDisplay : IBrailleDisplay
{
    public string Name => "No braille";
    public bool IsConnected => false;
    public int CellCount => 0;
    public void Write(ReadOnlyMemory<byte> cells, int? cursorPosition) { }

    public event EventHandler<BrailleKeyEventArgs>? KeyPressed
    {
        add { }
        remove { }
    }
}
