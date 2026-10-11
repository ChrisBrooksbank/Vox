using Vox.Core.Braille;

namespace Vox.Core.Tests.TestSupport;

/// <summary>
/// An <see cref="IBrailleDisplay"/> that records what is written to it, cut and padded to its cells
/// like a real display, and lets tests press its keys.
/// </summary>
public sealed class FakeBrailleDisplay : IBrailleDisplay
{
    private readonly object _lock = new();
    private readonly List<BrailleWrite> _writes = [];

    public FakeBrailleDisplay(int cellCount = 40)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cellCount);
        DisplayCells = cellCount;
    }

    /// <summary>One write as the display showed it.</summary>
    /// <param name="Cells">Exactly <see cref="CellCount"/> cells.</param>
    /// <param name="CursorPosition">The cursor cell passed with the write.</param>
    public sealed record BrailleWrite(byte[] Cells, int? CursorPosition)
    {
        /// <summary>The cells as Unicode braille, trailing blank cells removed.</summary>
        public string Unicode => BrailleDots.ToUnicode(Cells).TrimEnd('⠀');
    }

    public string Name => "Fake braille display";
    public bool IsConnected { get; set; } = true;

    /// <summary>The cells the display has, whether or not it is connected.</summary>
    public int DisplayCells { get; }

    public int CellCount => IsConnected ? DisplayCells : 0;

    /// <summary>Every write while connected, oldest first.</summary>
    public IReadOnlyList<BrailleWrite> Writes
    {
        get { lock (_lock) return _writes.ToList(); }
    }

    /// <summary>The last write, or null when nothing has been shown.</summary>
    public BrailleWrite? Current
    {
        get { lock (_lock) return _writes.Count == 0 ? null : _writes[^1]; }
    }

    public event EventHandler<BrailleKeyEventArgs>? KeyPressed;

    public void Write(ReadOnlyMemory<byte> cells, int? cursorPosition)
    {
        if (!IsConnected)
            return;
        var shown = new byte[DisplayCells];
        cells.Span[..Math.Min(cells.Length, DisplayCells)].CopyTo(shown);
        var cursor = cursorPosition is { } c && c >= 0 && c < DisplayCells ? cursorPosition : null;
        lock (_lock)
            _writes.Add(new BrailleWrite(shown, cursor));
    }

    /// <summary>Presses a chord of display keys ("dot1", "space", "panRight").</summary>
    public void Press(params string[] keys) => KeyPressed?.Invoke(this, new BrailleKeyEventArgs(keys));

    /// <summary>Presses the routing key over <paramref name="cell"/>.</summary>
    public void PressRouting(int cell)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cell);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cell, DisplayCells);
        KeyPressed?.Invoke(this, new BrailleKeyEventArgs(["routing"], cell));
    }
}
