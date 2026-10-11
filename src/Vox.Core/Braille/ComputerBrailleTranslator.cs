namespace Vox.Core.Braille;

/// <summary>
/// The built-in translator used when liblouis is not installed: 8-dot North American Braille
/// Computer Code (NABCC, US computer braille, as liblouis's en-us-comp8 table), one cell per
/// character. Letters are the literary letters; capitals and @ [ \ ] ^ add dot 7 to their
/// lower-case partners (` { | } ~), while _ is dots 4-5-6 and DEL dots 4-5-6-7 (as en-us-comp8);
/// control characters add dot 8 to the cell of the character 0x40 above them. Any character outside ASCII is written as <see cref="UnknownCell"/> (all eight
/// dots, a cell NABCC does not use) and that cell back-translates to '?'. Already uncontracted, so
/// there is no word to expand at the cursor.
/// </summary>
public sealed class ComputerBrailleTranslator : IBrailleTranslator
{
    /// <summary>The cell for a character outside ASCII: dots 1–8.</summary>
    public const byte UnknownCell = 0xFF;

    private static readonly byte[] AsciiCells = BuildAsciiCells();
    private static readonly Dictionary<byte, char> CellChars = BuildCellChars();

    public BrailleTranslation Translate(string text, int? cursorOffset = null)
    {
        var cells = new byte[text.Length];
        var map = new int[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            cells[i] = CellOf(text[i]);
            map[i] = i;
        }
        int? cursorCell = cursorOffset is { } offset ? Math.Clamp(offset, 0, text.Length) : null;
        return new BrailleTranslation(cells, map, (int[])map.Clone(), cursorCell);
    }

    public string BackTranslate(ReadOnlyMemory<byte> cells)
    {
        var text = new char[cells.Length];
        var span = cells.Span;
        for (var i = 0; i < span.Length; i++)
            text[i] = CellChars.TryGetValue(span[i], out var c) ? c : '?';
        return new string(text);
    }

    /// <summary>The NABCC cell of <paramref name="c"/>, or <see cref="UnknownCell"/> outside ASCII.</summary>
    public static byte CellOf(char c) => c < AsciiCells.Length ? AsciiCells[c] : UnknownCell;

    private static byte[] BuildAsciiCells()
    {
        // 0x20–0x3F: space, punctuation and digits
        string[] low =
        [
            "", "2346", "5", "3456", "1246", "146", "12346", "3",
            "12356", "23456", "16", "346", "6", "36", "46", "34",
            "356", "2", "23", "25", "256", "26", "235", "2356",
            "236", "35", "156", "56", "126", "123456", "345", "1456",
        ];
        // 0x60–0x7F: ` a–z { | } ~ DEL
        string[] high =
        [
            "4", "1", "12", "14", "145", "15", "124", "1245",
            "125", "24", "245", "13", "123", "134", "1345", "135",
            "1234", "12345", "1235", "234", "2345", "136", "1236", "2456",
            "1346", "13456", "1356", "246", "1256", "12456", "45", "456",
        ];

        var cells = new byte[128];
        for (var i = 0; i < 32; i++)
        {
            var upper = BrailleDots.FromDots(high[i]);
            cells[0x20 + i] = BrailleDots.FromDots(low[i]);
            cells[0x60 + i] = upper;
            cells[0x40 + i] = (byte)(upper | BrailleDots.Dot7);
            cells[i] = (byte)(upper | BrailleDots.Dot7 | BrailleDots.Dot8);
        }
        // Underscore is 4-5-6 without dot 7; DEL takes 4-5-6-7
        (cells['_'], cells[0x7F]) = (cells[0x7F], cells['_']);
        return cells;
    }

    private static Dictionary<byte, char> BuildCellChars()
    {
        var chars = new Dictionary<byte, char>();
        for (var c = 0; c < AsciiCells.Length; c++)
            chars.TryAdd(AsciiCells[c], (char)c);
        return chars;
    }
}
