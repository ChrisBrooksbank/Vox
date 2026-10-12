using System.Runtime.InteropServices;

namespace Vox.Core.Input;

/// <summary>A key press Vox sends to the focused application (from a braille display's keys).</summary>
/// <param name="VkCode">The virtual-key code.</param>
/// <param name="Modifiers">Shift, Ctrl and Alt held with it.</param>
public readonly record struct OutputKey(int VkCode, KeyModifiers Modifiers = KeyModifiers.None)
{
    private static readonly Dictionary<string, int> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Backspace"] = 0x08, ["Tab"] = 0x09, ["Enter"] = 0x0D, ["Escape"] = 0x1B, ["Space"] = 0x20,
        ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["End"] = 0x23, ["Home"] = 0x24,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Windows"] = 0x5B, ["Applications"] = 0x5D,
    };

    /// <summary>
    /// Reads a key as written in braille key maps: "Enter", "Shift+Tab", "Ctrl+Home", "Alt+F4",
    /// "A". Null when it isn't a key this knows.
    /// </summary>
    public static OutputKey? Parse(string text)
    {
        var modifiers = KeyModifiers.None;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;
        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "shift": modifiers |= KeyModifiers.Shift; break;
                case "ctrl" or "control": modifiers |= KeyModifiers.Ctrl; break;
                case "alt": modifiers |= KeyModifiers.Alt; break;
                default: return null;
            }
        }
        var key = parts[^1];
        if (Names.TryGetValue(key, out var vk))
            return new OutputKey(vk, modifiers);
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
            return new OutputKey(char.ToUpperInvariant(key[0]), modifiers);
        if (key.Length is 2 or 3 && (key[0] is 'F' or 'f') && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24)
            return new OutputKey(0x70 + f - 1, modifiers);
        return null;
    }
}

/// <summary>Sends keys and text to the focused application, as if typed.</summary>
public interface IKeyboardOutput
{
    void SendKey(OutputKey key);

    /// <summary>Types <paramref name="text"/> character by character (Unicode input, any layout).</summary>
    void TypeText(string text);
}

/// <summary>
/// <see cref="IKeyboardOutput"/> through SendInput. The keys go through Vox's own keyboard hook
/// like typed ones, so an arrow key sent in browse mode moves the browse cursor.
/// </summary>
public sealed class SendInputKeyboardOutput : IKeyboardOutput, Remote.IRemoteKeyInjector
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    public void SendKey(OutputKey key)
    {
        var inputs = new List<INPUT>();
        var modifiers = new List<ushort>();
        if (key.Modifiers.HasFlag(KeyModifiers.Ctrl)) modifiers.Add(0x11);
        if (key.Modifiers.HasFlag(KeyModifiers.Alt)) modifiers.Add(0x12);
        if (key.Modifiers.HasFlag(KeyModifiers.Shift)) modifiers.Add(0x10);
        foreach (var modifier in modifiers)
            inputs.Add(Key(modifier, 0));
        var flags = IsExtended(key.VkCode) ? KEYEVENTF_EXTENDEDKEY : 0;
        inputs.Add(Key((ushort)key.VkCode, flags));
        inputs.Add(Key((ushort)key.VkCode, flags | KEYEVENTF_KEYUP));
        for (var i = modifiers.Count - 1; i >= 0; i--)
            inputs.Add(Key(modifiers[i], KEYEVENTF_KEYUP));
        Send(inputs);
    }

    public void TypeText(string text)
    {
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (var c in text)
        {
            if (c == '\n')
            {
                inputs.Add(Key(0x0D, 0));
                inputs.Add(Key(0x0D, KEYEVENTF_KEYUP));
                continue;
            }
            inputs.Add(Unicode(c, 0));
            inputs.Add(Unicode(c, KEYEVENTF_KEYUP));
        }
        Send(inputs);
    }

    // Navigation keys are on the extended part of the keyboard (not the keypad)
    /// <summary>One key going down or up, as pressed on the computer controlling this one (through Vox's hook, like typed keys).</summary>
    public void SendRaw(int vkCode, int scanCode, bool down, bool extended)
    {
        var flags = (extended ? KEYEVENTF_EXTENDEDKEY : 0) | (down ? 0 : KEYEVENTF_KEYUP);
        Send([new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vkCode, wScan = (ushort)scanCode, dwFlags = flags } } }]);
    }

    private static bool IsExtended(int vk) => vk is >= 0x21 and <= 0x28 or 0x2D or 0x2E or 0x5B or 0x5D;

    private static INPUT Key(ushort vk, uint flags) =>
        new() { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } } };

    private static INPUT Unicode(char c, uint flags) =>
        new() { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | flags } } };

    private static void Send(List<INPUT> inputs)
    {
        if (inputs.Count == 0 || !OperatingSystem.IsWindows())
            return;
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    // Sized as the largest member (MOUSEINPUT) so Marshal.SizeOf<INPUT> matches the native size
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }
}
