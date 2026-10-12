namespace Vox.Sdk;

/// <summary>
/// A command an add-on adds: run on the pipeline thread when one of its <see cref="Gestures"/> is
/// pressed. <see cref="Name"/> and <see cref="Description"/> are what input help says and the
/// add-ons dialog lists. Keys of an app module's commands apply only while it is active and come
/// before Vox's own keys; global commands' keys come after the active app module's but also before
/// Vox's own.
/// </summary>
/// <param name="Id">Unique within the add-on ("readTotals").</param>
public sealed record AddOnCommand(string Id, string Name, string Description, Action Run)
{
    public IReadOnlyList<KeyGesture> Gestures { get; init; } = [];
}

/// <summary>Modifier keys of a <see cref="KeyGesture"/>. <see cref="VoxKey"/> is the screen reader key (Insert or Caps Lock, as the user chose).</summary>
[Flags]
public enum GestureModifiers
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
    VoxKey = 8,
}

/// <summary>Where a gesture applies: everywhere, or on web pages in browse or focus mode only.</summary>
public enum GestureMode
{
    Any,
    Browse,
    Focus,
}

/// <summary>A key with modifiers: <c>KeyGesture.Parse("VoxKey+Shift+J")</c>.</summary>
/// <param name="VirtualKey">The Windows virtual-key code.</param>
public readonly record struct KeyGesture(GestureModifiers Modifiers, int VirtualKey, GestureMode Mode = GestureMode.Any)
{
    private static readonly Dictionary<string, int> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Backspace"] = 0x08, ["Tab"] = 0x09, ["Enter"] = 0x0D, ["Escape"] = 0x1B, ["Space"] = 0x20,
        ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["End"] = 0x23, ["Home"] = 0x24, ["Left"] = 0x25,
        ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28, ["Insert"] = 0x2D, ["Delete"] = 0x2E,
        ["Applications"] = 0x5D, ["Semicolon"] = 0xBA, ["Equals"] = 0xBB, ["Comma"] = 0xBC, ["Minus"] = 0xBD,
        ["Period"] = 0xBE, ["Slash"] = 0xBF, ["Grave"] = 0xC0, ["LeftBracket"] = 0xDB, ["Backslash"] = 0xDC,
        ["RightBracket"] = 0xDD, ["Quote"] = 0xDE,
    };

    /// <summary>
    /// Reads a gesture written as modifiers and a key joined by '+': modifiers are Shift, Ctrl, Alt
    /// and VoxKey (or Insert, the screen reader key); the key a letter, a digit, F1–F24, NumPad0–NumPad9
    /// or a name (Enter, Space, PageDown, Comma, ...). Throws <see cref="FormatException"/>.
    /// </summary>
    public static KeyGesture Parse(string text, GestureMode mode = GestureMode.Any) =>
        TryParse(text, out var gesture, mode) ? gesture : throw new FormatException($"'{text}' is not a key gesture.");

    public static bool TryParse(string? text, out KeyGesture gesture, GestureMode mode = GestureMode.Any)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;
        var modifiers = GestureModifiers.None;
        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "shift": modifiers |= GestureModifiers.Shift; break;
                case "ctrl" or "control": modifiers |= GestureModifiers.Ctrl; break;
                case "alt": modifiers |= GestureModifiers.Alt; break;
                case "voxkey" or "insert": modifiers |= GestureModifiers.VoxKey; break;
                default: return false;
            }
        }
        if (KeyCode(parts[^1]) is not { } vk)
            return false;
        gesture = new KeyGesture(modifiers, vk, mode);
        return true;
    }

    private static int? KeyCode(string key)
    {
        if (Named.TryGetValue(key, out var vk))
            return vk;
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
            return char.ToUpperInvariant(key[0]);
        if (key.Length is 2 or 3 && key[0] is 'F' or 'f' && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24)
            return 0x70 + f - 1;
        if (key.Length == 7 && key.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) && char.IsAsciiDigit(key[6]))
            return 0x60 + (key[6] - '0');
        return null;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(GestureModifiers.VoxKey)) parts.Add("VoxKey");
        if (Modifiers.HasFlag(GestureModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(GestureModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(GestureModifiers.Shift)) parts.Add("Shift");
        var vk = VirtualKey;
        var name = Named.FirstOrDefault(n => n.Value == vk).Key;
        parts.Add(name ?? VirtualKey switch
        {
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)VirtualKey).ToString(),
            >= 0x60 and <= 0x69 => $"NumPad{VirtualKey - 0x60}",
            >= 0x70 and <= 0x87 => $"F{VirtualKey - 0x6F}",
            _ => $"0x{VirtualKey:X2}",
        });
        return string.Join('+', parts);
    }
}
