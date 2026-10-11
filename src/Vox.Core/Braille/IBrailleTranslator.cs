namespace Vox.Core.Braille;

/// <summary>
/// Text translated to braille, with the mapping between the two: contracted braille has fewer cells
/// than characters ("the" is one cell), and a capital or number sign adds a cell.
/// </summary>
/// <param name="Cells">The braille cells (see <see cref="BrailleDots"/>).</param>
/// <param name="CellForChar">For each character of the text, the first cell it was translated into (one entry per character).</param>
/// <param name="CharForCell">For each cell, the first character of the text it came from (one entry per cell); a routing key uses this.</param>
/// <param name="CursorCell">The cell of the requested cursor offset, or null when no cursor was given.</param>
public sealed record BrailleTranslation(byte[] Cells, int[] CellForChar, int[] CharForCell, int? CursorCell)
{
    public static readonly BrailleTranslation Empty = new([], [], [], null);

    /// <summary>
    /// The cell showing text offset <paramref name="offset"/>; an offset at or past the end of the text
    /// is the cell after the last one (where the caret sits at the end of a line).
    /// </summary>
    public int CellOf(int offset)
    {
        if (offset < 0)
            return 0;
        return offset < CellForChar.Length ? CellForChar[offset] : Cells.Length;
    }

    /// <summary>
    /// The text offset shown at <paramref name="cell"/>; a cell past the end is the offset after the
    /// last character.
    /// </summary>
    public int OffsetOf(int cell)
    {
        if (cell < 0)
            return 0;
        return cell < CharForCell.Length ? CharForCell[cell] : CellForChar.Length;
    }
}

/// <summary>
/// Translates text to braille and back (liblouis, or a fake in tests). The braille table is the
/// translator's own; a table change in settings makes a new translator.
/// </summary>
public interface IBrailleTranslator
{
    /// <summary>
    /// Translates <paramref name="text"/> forward. <paramref name="cursorOffset"/> is a text offset
    /// (0 to the text's length) whose cell is reported as <see cref="BrailleTranslation.CursorCell"/>.
    /// </summary>
    BrailleTranslation Translate(string text, int? cursorOffset = null);

    /// <summary>Back-translates cells typed on a braille keyboard to text.</summary>
    string BackTranslate(ReadOnlyMemory<byte> cells);
}
