namespace Vox.E2E.Tests;

/// <summary>
/// Keys written as people write them ("H", "Shift+H", "Insert+Shift+D", "Ctrl+Alt+Right",
/// "Enter", "F7"), turned into virtual-key presses for <see cref="KeyInjector"/>.
/// </summary>
public static class KeyNotation
{
    private static readonly Dictionary<string, ushort> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Shift"] = KeyInjector.Shift, ["Ctrl"] = KeyInjector.Control, ["Control"] = KeyInjector.Control,
        ["Alt"] = KeyInjector.Alt, ["Insert"] = KeyInjector.Insert, ["Enter"] = KeyInjector.Enter,
        ["Escape"] = KeyInjector.Escape, ["Esc"] = KeyInjector.Escape, ["Tab"] = KeyInjector.Tab,
        ["Space"] = 0x20, ["Left"] = KeyInjector.Left, ["Up"] = KeyInjector.Up, ["Right"] = KeyInjector.Right,
        ["Down"] = KeyInjector.Down, ["Home"] = KeyInjector.Home, ["End"] = KeyInjector.End,
        ["Delete"] = 0x2E, ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["Backspace"] = 0x08,
        ["Comma"] = 0xBC, ["Period"] = 0xBE,
    };

    /// <summary>The key and the modifiers held for it.</summary>
    public static (ushort Key, ushort[] Modifiers) Parse(string notation)
    {
        var parts = notation.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            throw new FormatException($"No key in \"{notation}\"");
        var modifiers = parts[..^1].Select(Code).ToArray();
        return (Code(parts[^1]), modifiers);
    }

    public static void Press(string notation)
    {
        var (key, modifiers) = Parse(notation);
        KeyInjector.Press(key, modifiers);
    }

    private static ushort Code(string name)
    {
        if (Names.TryGetValue(name, out var code))
            return code;
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]))
            return char.ToUpperInvariant(name[0]);
        if (name.Length is 2 or 3 && (name[0] is 'F' or 'f') && int.TryParse(name[1..], out var f) && f is >= 1 and <= 12)
            return (ushort)(0x70 + f - 1);
        throw new FormatException($"Unknown key \"{name}\"");
    }
}
