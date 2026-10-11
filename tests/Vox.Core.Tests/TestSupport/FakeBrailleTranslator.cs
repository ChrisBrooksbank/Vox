using Vox.Core.Braille;

namespace Vox.Core.Tests.TestSupport;

/// <summary>
/// An <see cref="IBrailleTranslator"/> with a fixed, predictable table: one cell per character
/// (letters as in UEB, capitals with dot 7, digits and . , as in North American computer braille,
/// a blank cell for a space, a full six-dot cell for anything else), plus whole-word contractions
/// when <see cref="Contracted"/> is on, so tests see cells and text of different lengths.
/// </summary>
public sealed class FakeBrailleTranslator : IBrailleTranslator
{
    /// <summary>The cell for a character the table doesn't know; back-translated as '?'.</summary>
    public static readonly byte UnknownCell = BrailleDots.FromDots("123456");

    private static readonly Dictionary<char, byte> CharCells = BuildCharCells();
    private static readonly Dictionary<byte, char> CellChars = CharCells
        .GroupBy(kv => kv.Value)
        .ToDictionary(g => g.Key, g => g.First().Key);

    /// <summary>When true, the words in <see cref="Contractions"/> are written as their single cell.</summary>
    public bool Contracted { get; set; }

    /// <summary>Whole words (lower case) written as one cell when <see cref="Contracted"/> is on.</summary>
    public Dictionary<string, byte> Contractions { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["the"] = BrailleDots.FromDots("2346"),
        ["and"] = BrailleDots.FromDots("12346"),
    };

    /// <summary>Every text translated, oldest first.</summary>
    public List<string> Translated { get; } = [];

    public BrailleTranslation Translate(string text, int? cursorOffset = null)
    {
        Translated.Add(text);
        var cells = new List<byte>(text.Length);
        var charForCell = new List<int>(text.Length);
        var cellForChar = new int[text.Length];

        var i = 0;
        while (i < text.Length)
        {
            if (Contracted && TryContraction(text, i, out var length, out var cell))
            {
                for (var j = i; j < i + length; j++)
                    cellForChar[j] = cells.Count;
                charForCell.Add(i);
                cells.Add(cell);
                i += length;
                continue;
            }

            cellForChar[i] = cells.Count;
            charForCell.Add(i);
            cells.Add(CellOf(text[i]));
            i++;
        }

        var translation = new BrailleTranslation(cells.ToArray(), cellForChar, charForCell.ToArray(), null);
        return cursorOffset is { } offset
            ? translation with { CursorCell = translation.CellOf(Math.Clamp(offset, 0, text.Length)) }
            : translation;
    }

    public string BackTranslate(ReadOnlyMemory<byte> cells)
    {
        var text = new System.Text.StringBuilder(cells.Length);
        foreach (var cell in cells.Span)
        {
            var contraction = Contracted ? Contractions.FirstOrDefault(kv => kv.Value == cell).Key : null;
            if (contraction is not null)
                text.Append(contraction);
            else if (CellChars.TryGetValue(cell, out var c))
                text.Append(c);
            else if ((cell & BrailleDots.Dot7) != 0 && CellChars.TryGetValue((byte)(cell & ~BrailleDots.Dot7), out var lower) && char.IsLetter(lower))
                text.Append(char.ToUpperInvariant(lower));
            else
                text.Append('?');
        }
        return text.ToString();
    }

    private bool TryContraction(string text, int start, out int length, out byte cell)
    {
        length = 0;
        cell = 0;
        if (start > 0 && char.IsLetterOrDigit(text[start - 1]))
            return false;
        var end = start;
        while (end < text.Length && char.IsLetterOrDigit(text[end]))
            end++;
        if (end == start || !Contractions.TryGetValue(text[start..end], out cell))
            return false;
        length = end - start;
        return true;
    }

    private static byte CellOf(char c)
    {
        if (CharCells.TryGetValue(c, out var cell))
            return cell;
        if (char.IsUpper(c) && CharCells.TryGetValue(char.ToLowerInvariant(c), out var lower))
            return (byte)(lower | BrailleDots.Dot7);
        return UnknownCell;
    }

    private static Dictionary<char, byte> BuildCharCells()
    {
        string[] letters =
        [
            "1", "12", "14", "145", "15", "124", "1245", "125", "24", "245",
            "13", "123", "134", "1345", "135", "1234", "12345", "1235", "234", "2345",
            "136", "1236", "2456", "1346", "13456", "1356",
        ];
        string[] digits = ["356", "2", "23", "25", "256", "26", "235", "2356", "236", "35"];

        var cells = new Dictionary<char, byte> { [' '] = 0, ['.'] = BrailleDots.FromDots("46"), [','] = BrailleDots.FromDots("6") };
        for (var i = 0; i < letters.Length; i++)
            cells[(char)('a' + i)] = BrailleDots.FromDots(letters[i]);
        for (var i = 0; i < digits.Length; i++)
            cells[(char)('0' + i)] = BrailleDots.FromDots(digits[i]);
        return cells;
    }
}
