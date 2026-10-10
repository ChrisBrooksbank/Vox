namespace Vox.Core.Input;

/// <summary>
/// Input help (Insert+1): while it is on, keys say what they do instead of doing it. This says
/// keys and commands as people read them.
/// </summary>
public static class InputHelp
{
    /// <summary>What a key does: the command's name and description, or just the key's name when it does nothing.</summary>
    public static string Describe(NavigationCommand? command, KeyModifiers modifiers, int vkCode, bool isKeypad, string screenReaderKey = "Insert")
    {
        var key = KeyName(modifiers, vkCode, isKeypad, screenReaderKey);
        if (command is not { } bound)
            return key;
        var info = CommandCatalog.Describe(bound);
        return $"{key}, {info.Name}. {info.Description}";
    }

    /// <summary>True for keys that only modify others (Shift, Ctrl, Alt, Windows): help says nothing for them alone.</summary>
    public static bool IsModifierKey(int vkCode) =>
        vkCode is 0x10 or 0x11 or 0x12 or >= 0xA0 and <= 0xA5 or 0x5B or 0x5C;

    /// <summary>"Insert+Shift+D", "Ctrl+Alt+Right Arrow", "Numpad 8".</summary>
    public static string KeyName(KeyModifiers modifiers, int vkCode, bool isKeypad = false, string screenReaderKey = "Insert")
    {
        var parts = new List<string>();
        if ((modifiers & KeyModifiers.Insert) != 0) parts.Add(screenReaderKey);
        if ((modifiers & KeyModifiers.Ctrl) != 0) parts.Add("Ctrl");
        if ((modifiers & KeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((modifiers & KeyModifiers.Shift) != 0) parts.Add("Shift");
        parts.Add(Name(vkCode, isKeypad));
        return string.Join("+", parts);
    }

    private static string Name(int vk, bool isKeypad)
    {
        // Keypad keys with Num Lock off arrive as navigation keys
        if (isKeypad)
        {
            var keypad = vk switch
            {
                0x2D => "0", 0x23 => "1", 0x28 => "2", 0x22 => "3", 0x25 => "4", 0x0C => "5",
                0x27 => "6", 0x24 => "7", 0x26 => "8", 0x21 => "9", 0x2E => "Period", 0x0D => "Enter",
                _ => null,
            };
            if (keypad is not null)
                return "Numpad " + keypad;
        }
        return vk switch
        {
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            >= 0x60 and <= 0x69 => "Numpad " + (vk - 0x60),
            >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
            0x6A => "Numpad Star", 0x6B => "Numpad Plus", 0x6D => "Numpad Minus", 0x6E => "Numpad Period", 0x6F => "Numpad Slash",
            0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x1B => "Escape", 0x20 => "Space",
            0x21 => "Page Up", 0x22 => "Page Down", 0x23 => "End", 0x24 => "Home",
            0x25 => "Left Arrow", 0x26 => "Up Arrow", 0x27 => "Right Arrow", 0x28 => "Down Arrow",
            0x2D => "Insert", 0x2E => "Delete", 0x14 => "Caps Lock", 0x90 => "Num Lock", 0x91 => "Scroll Lock",
            0x5D => "Applications", 0x2C => "Print Screen", 0x13 => "Pause",
            0xBA => "Semicolon", 0xBB => "Equals", 0xBC => "Comma", 0xBD => "Minus", 0xBE => "Period",
            0xBF => "Slash", 0xC0 => "Grave", 0xDB => "Left Bracket", 0xDC => "Backslash",
            0xDD => "Right Bracket", 0xDE => "Apostrophe",
            269 => "Numpad Enter",
            _ => $"key {vk}",
        };
    }
}
