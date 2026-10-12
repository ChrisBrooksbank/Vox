using System.Runtime.InteropServices;

namespace Vox.E2E.Tests;

/// <summary>
/// Presses keys through SendInput, so they go through Vox's keyboard hook as typed keys would.
/// Navigation keys are sent as extended keys (Insert, not keypad 0).
/// </summary>
public static class KeyInjector
{
    public const ushort Shift = 0x10, Control = 0x11, Alt = 0x12, Insert = 0x2D, Enter = 0x0D, Escape = 0x1B, Tab = 0x09;
    public const ushort Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, Home = 0x24, End = 0x23;

    private static readonly HashSet<ushort> ExtendedKeys = [Insert, Left, Up, Right, Down, Home, End, 0x2E, 0x21, 0x22];

    /// <summary>Presses <paramref name="key"/> (a virtual-key code, or a letter) with the given modifiers held.</summary>
    public static void Press(ushort key, params ushort[] modifiers)
    {
        var inputs = new List<Input>();
        foreach (var modifier in modifiers)
            inputs.Add(KeyInput(modifier, up: false));
        inputs.Add(KeyInput(key, up: false));
        inputs.Add(KeyInput(key, up: true));
        foreach (var modifier in modifiers.Reverse())
            inputs.Add(KeyInput(modifier, up: true));
        Send([.. inputs]);
    }

    /// <summary>Presses a letter or digit key ('H', '1').</summary>
    public static void Press(char key, params ushort[] modifiers) => Press((ushort)char.ToUpperInvariant(key), modifiers);

    /// <summary>
    /// The marker Vox puts on keys it injects itself (KeyboardHook.VoxInjectedMarker): its hook lets
    /// them through without tracking them, so a key-up sent with it is one the hook never sees, as
    /// when the secure desktop takes a key-up away.
    /// </summary>
    public const nuint UnseenByVox = 0x566F78;

    /// <summary>Holds <paramref name="key"/> down without letting it go.</summary>
    public static void KeyDown(ushort key, nuint extraInfo = 0) => Send([KeyInput(key, up: false, extraInfo)]);

    /// <summary>Lets <paramref name="key"/> go (with <see cref="UnseenByVox"/>, behind the hook's back).</summary>
    public static void KeyUp(ushort key, nuint extraInfo = 0) => Send([KeyInput(key, up: true, extraInfo)]);

    /// <summary>Types <paramref name="text"/> as Unicode characters (VK_PACKET), whatever the keyboard layout.</summary>
    public static void Type(string text)
    {
        var inputs = new List<Input>();
        foreach (var c in text)
        {
            inputs.Add(new Input { Type = 1, Keyboard = new KeyboardInput { ScanCode = c, Flags = KeyEventUnicode } });
            inputs.Add(new Input { Type = 1, Keyboard = new KeyboardInput { ScanCode = c, Flags = KeyEventUnicode | KeyEventUp } });
        }
        Send([.. inputs]);
    }

    private const uint KeyEventExtended = 0x0001, KeyEventUp = 0x0002, KeyEventUnicode = 0x0004;

    private static void Send(Input[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new InvalidOperationException("SendInput was blocked (is the desktop locked, or a window of higher integrity in front?)");
    }

    private static Input KeyInput(ushort key, bool up, nuint extraInfo = 0) => new()
    {
        Type = 1, // INPUT_KEYBOARD
        Keyboard = new KeyboardInput
        {
            VirtualKey = key,
            Flags = (up ? KeyEventUp : 0) | (ExtendedKeys.Contains(key) ? KeyEventExtended : 0),
            ExtraInfo = (IntPtr)extraInfo,
        },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
        // INPUT is a union whose largest member (MOUSEINPUT) is 8 bytes longer than KEYBDINPUT
        private readonly long _padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public KeyboardInput Keyboard;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
