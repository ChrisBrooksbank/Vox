using Vox.Core.Configuration;
using Vox.Core.Localization;

namespace Vox.Core.Input;

/// <summary>
/// Input help (Insert+1): while it is on, keys say what they do instead of doing it. This says
/// keys and commands as people read them.
/// </summary>
public static class InputHelp
{
    /// <summary>
    /// What a key does: the command's name and description, or just the key's name when it does
    /// nothing. <paramref name="screenReaderKey"/> names the Vox key (null: Insert).
    /// </summary>
    public static string Describe(NavigationCommand? command, KeyModifiers modifiers, int vkCode, bool isKeypad, string? screenReaderKey = null)
    {
        var key = KeyName(modifiers, vkCode, isKeypad, screenReaderKey);
        if (command is not { } bound)
            return key;
        var info = CommandCatalog.Describe(bound);
        return VoxStrings.Format("InputHelp.KeyAndCommand", key, info.Name, info.Description);
    }

    /// <summary>The name of the screen reader key chosen: "Insert" or "Caps Lock", in the language in use.</summary>
    public static string ScreenReaderKeyName(ModifierKey modifierKey) =>
        VoxStrings.Get(modifierKey == ModifierKey.CapsLock ? "Key.CapsLock" : "Key.Insert");

    /// <summary>True for keys that only modify others (Shift, Ctrl, Alt, Windows): help says nothing for them alone.</summary>
    public static bool IsModifierKey(int vkCode) =>
        vkCode is 0x10 or 0x11 or 0x12 or >= 0xA0 and <= 0xA5 or 0x5B or 0x5C;

    /// <summary>"Insert+Shift+D", "Ctrl+Alt+Right Arrow", "Numpad 8" (the Key.* strings).</summary>
    public static string KeyName(KeyModifiers modifiers, int vkCode, bool isKeypad = false, string? screenReaderKey = null)
    {
        var parts = new List<string>();
        if ((modifiers & KeyModifiers.Insert) != 0) parts.Add(screenReaderKey ?? VoxStrings.Get("Key.Insert"));
        if ((modifiers & KeyModifiers.Ctrl) != 0) parts.Add(VoxStrings.Get("Key.Ctrl"));
        if ((modifiers & KeyModifiers.Alt) != 0) parts.Add(VoxStrings.Get("Key.Alt"));
        if ((modifiers & KeyModifiers.Shift) != 0) parts.Add(VoxStrings.Get("Key.Shift"));
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
                0x27 => "6", 0x24 => "7", 0x26 => "8", 0x21 => "9", 0x2E => "Key.NumpadPeriod", 0x0D => "Key.NumpadEnter",
                _ => null,
            };
            if (keypad is not null)
                return keypad.Length == 1 ? VoxStrings.Format("Key.NumpadDigit", keypad) : VoxStrings.Get(keypad);
        }
        return vk switch
        {
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            >= 0x60 and <= 0x69 => VoxStrings.Format("Key.NumpadDigit", vk - 0x60),
            >= 0x70 and <= 0x87 => VoxStrings.Format("Key.Function", vk - 0x6F),
            _ => KeyNames.TryGetValue(vk, out var name) ? VoxStrings.Get("Key." + name) : VoxStrings.Format("Key.Unknown", vk),
        };
    }

    // Keys with a name of their own: the Key.<name> string
    private static readonly Dictionary<int, string> KeyNames = new()
    {
        [0x6A] = "NumpadStar", [0x6B] = "NumpadPlus", [0x6D] = "NumpadMinus", [0x6E] = "NumpadPeriod", [0x6F] = "NumpadSlash",
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x1B] = "Escape", [0x20] = "Space",
        [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home",
        [0x25] = "LeftArrow", [0x26] = "UpArrow", [0x27] = "RightArrow", [0x28] = "DownArrow",
        [0x2D] = "Insert", [0x2E] = "Delete", [0x14] = "CapsLock", [0x90] = "NumLock", [0x91] = "ScrollLock",
        [0x5D] = "Applications", [0x2C] = "PrintScreen", [0x13] = "Pause",
        [0xBA] = "Semicolon", [0xBB] = "Equals", [0xBC] = "Comma", [0xBD] = "Minus", [0xBE] = "Period",
        [0xBF] = "Slash", [0xC0] = "Grave", [0xDB] = "LeftBracket", [0xDC] = "Backslash",
        [0xDD] = "RightBracket", [0xDE] = "Apostrophe",
        [269] = "NumpadEnter",
    };
}
