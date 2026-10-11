namespace Vox.Core.Braille;

/// <summary>
/// Braille cells as bytes: bit <c>n - 1</c> raises dot <c>n</c> (dot 1 = 0x01 … dot 8 = 0x80), the
/// layout of the Unicode braille block (U+2800) and of the HID braille and BrlAPI cell formats.
/// </summary>
public static class BrailleDots
{
    public const byte Dot1 = 0x01;
    public const byte Dot2 = 0x02;
    public const byte Dot3 = 0x04;
    public const byte Dot4 = 0x08;
    public const byte Dot5 = 0x10;
    public const byte Dot6 = 0x20;
    public const byte Dot7 = 0x40;
    public const byte Dot8 = 0x80;

    /// <summary>Dots 7 and 8, the cursor and selection underline.</summary>
    public const byte Dots78 = Dot7 | Dot8;

    private const char UnicodeBase = '⠀';

    /// <summary>The cell with the given dots (1–8) raised, as in <c>FromDots("125")</c> for the letter h.</summary>
    public static byte FromDots(string dots)
    {
        byte cell = 0;
        foreach (var c in dots)
        {
            if (c is < '1' or > '8')
                throw new ArgumentException($"'{c}' is not a braille dot (1-8).", nameof(dots));
            cell |= (byte)(1 << (c - '1'));
        }
        return cell;
    }

    /// <summary>The raised dots of <paramref name="cell"/> in order, as in "125"; empty for a blank cell.</summary>
    public static string ToDots(byte cell)
    {
        var dots = new System.Text.StringBuilder(8);
        for (var dot = 0; dot < 8; dot++)
        {
            if ((cell & (1 << dot)) != 0)
                dots.Append((char)('1' + dot));
        }
        return dots.ToString();
    }

    /// <summary>The cells as Unicode braille patterns (U+2800–U+28FF), for logs, the braille viewer and tests.</summary>
    public static string ToUnicode(ReadOnlySpan<byte> cells)
    {
        var chars = new char[cells.Length];
        for (var i = 0; i < cells.Length; i++)
            chars[i] = (char)(UnicodeBase + cells[i]);
        return new string(chars);
    }

    /// <summary>
    /// The cells of a string of Unicode braille patterns (U+2800–U+28FF); a plain space is a blank cell.
    /// </summary>
    public static byte[] FromUnicode(string braille)
    {
        var cells = new byte[braille.Length];
        for (var i = 0; i < braille.Length; i++)
        {
            var c = braille[i];
            if (c == ' ')
                continue;
            if (c is < UnicodeBase or > '⣿')
                throw new ArgumentException($"'{c}' is not a Unicode braille pattern.", nameof(braille));
            cells[i] = (byte)(c - UnicodeBase);
        }
        return cells;
    }
}
