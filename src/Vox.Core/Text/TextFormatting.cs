namespace Vox.Core.Text;

/// <summary>How text formatting is spoken (the Read formatting command).</summary>
public static class TextFormatting
{
    /// <summary>
    /// "Calibri, 11 point, bold, italic, underlined, red, misspelled"; only what is known, and
    /// "No formatting information" when nothing is.
    /// </summary>
    public static string Describe(TextAttributes attributes)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(attributes.FontName))
            parts.Add(attributes.FontName);
        if (attributes.FontSize is { } size)
            parts.Add($"{size:0.#} point");
        if (attributes.IsBold == true)
            parts.Add("bold");
        if (attributes.IsItalic == true)
            parts.Add("italic");
        if (attributes.IsUnderline == true)
            parts.Add("underlined");
        if (attributes.ForegroundColor is { } color && ColorName(color) is { } name)
            parts.Add(name);
        if (attributes.IsSpellingError)
            parts.Add("misspelled");
        if (attributes.IsGrammarError)
            parts.Add("grammar error");
        return parts.Count == 0 ? "No formatting information" : string.Join(", ", parts);
    }

    private static readonly (string Name, int R, int G, int B)[] Colors =
    [
        ("black", 0, 0, 0), ("white", 255, 255, 255), ("gray", 128, 128, 128), ("red", 220, 20, 20),
        ("dark red", 128, 0, 0), ("orange", 255, 140, 0), ("yellow", 255, 220, 0), ("green", 0, 160, 0),
        ("dark green", 0, 100, 0), ("blue", 0, 0, 230), ("dark blue", 0, 0, 128), ("light blue", 100, 180, 255),
        ("purple", 128, 0, 128), ("pink", 255, 105, 180), ("brown", 140, 80, 20),
    ];

    /// <summary>The nearest basic colour name for "#RRGGBB", or null if it can't be parsed.</summary>
    public static string? ColorName(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#' || !int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return null;
        int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
        return Colors.MinBy(c => (c.R - r) * (c.R - r) + (c.G - g) * (c.G - g) + (c.B - b) * (c.B - b)).Name;
    }
}
