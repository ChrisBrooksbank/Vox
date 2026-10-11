namespace Vox.Core.AppModules.Excel;

/// <summary>A1-style cell addresses: column letters to numbers and back.</summary>
public static class ExcelAddress
{
    /// <summary>"B5" for row 5, column 2.</summary>
    public static string Format(int row, int column) => ColumnLetters(column) + row;

    /// <summary>The letters of a 1-based column: 1 → "A", 27 → "AA".</summary>
    public static string ColumnLetters(int column)
    {
        if (column < 1)
            throw new ArgumentOutOfRangeException(nameof(column));
        var letters = new Stack<char>();
        while (column > 0)
        {
            column--;
            letters.Push((char)('A' + column % 26));
            column /= 26;
        }
        return new string([.. letters]);
    }

    /// <summary>The 1-based column of letters such as "AA", or 0 when they aren't column letters.</summary>
    public static int ColumnNumber(string letters)
    {
        int column = 0;
        foreach (var c in letters.Trim())
        {
            if (!char.IsAsciiLetter(c))
                return 0;
            column = column * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }
        return column;
    }

    /// <summary>
    /// A range address as said: "A1:C5" → "A1 to C5"; Excel's absolute marks ("$A$1") are left out.
    /// </summary>
    public static string Speakable(string address) =>
        address.Replace("$", string.Empty).Replace(":", " to ").Replace(",", ", ");
}
