using System.Runtime.InteropServices;
using System.Text;

namespace Vox.Core.Input;

/// <summary>
/// What a key press types: a character, or a dead key (an accent that combines with the next
/// character, e.g. '^' then 'e' types 'ê'). <see cref="Char"/> is '\0' when nothing is typed.
/// </summary>
public readonly record struct TypedChar(char Char, bool IsDeadKey = false)
{
    public static readonly TypedChar None = new('\0');

    public static implicit operator TypedChar(char c) => new(c);
}

/// <summary>
/// Converts a key press to the character it types, using the keyboard layout of the foreground
/// window (so a UK layout's Shift+2 is '"', not '@'). Uses ToUnicodeEx with flag 0x4 so the
/// system keyboard state — including pending dead keys — is not changed (Windows 10 1607+).
/// </summary>
public static class KeyboardLayoutMapper
{
    private const uint DontChangeKeyboardState = 0x4;
    private const uint MAPVK_VK_TO_VSC = 0;

    [DllImport("user32.dll")]
    private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff, uint wFlags, nint dwhkl);

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    /// <summary>
    /// The character typed by <paramref name="key"/> in the foreground window's layout (nothing
    /// when it types nothing; flagged when it is a dead key). Ctrl+Alt is passed as AltGr, so
    /// AltGr characters ('@', '€', '{' on many layouts) are found. Falls back to the US table
    /// off Windows.
    /// </summary>
    public static TypedChar ToChar(KeyEvent key)
    {
        if (!OperatingSystem.IsWindows())
            return TypingEchoHandler.VkCodeToChar(key.VkCode, key.Modifiers, key.CapsLockOn);

        try
        {
            var state = new byte[256];
            if ((key.Modifiers & KeyModifiers.Shift) != 0)
                state[KeyStateTracker.VK_SHIFT] = state[KeyStateTracker.VK_LSHIFT] = 0x80;
            if (key.CapsLockOn)
                state[KeyStateTracker.VK_CAPITAL] = 0x01;
            if ((key.Modifiers & KeyModifiers.Ctrl) != 0 && (key.Modifiers & KeyModifiers.Alt) != 0)
            {
                // AltGr (Windows reports it as Ctrl+Alt)
                state[KeyStateTracker.VK_CONTROL] = state[KeyStateTracker.VK_LCONTROL] = 0x80;
                state[KeyStateTracker.VK_MENU] = state[KeyStateTracker.VK_RMENU] = 0x80;
            }

            var layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out _));
            var scanCode = key.ScanCode != 0 ? (uint)key.ScanCode : MapVirtualKey((uint)key.VkCode, MAPVK_VK_TO_VSC);
            var buffer = new StringBuilder(4);
            int count = ToUnicodeEx((uint)key.VkCode, scanCode, state, buffer, buffer.Capacity, DontChangeKeyboardState, layout);
            if (count < 0 && buffer.Length > 0)
                return new TypedChar(buffer[0], IsDeadKey: true); // the accent's spacing form
            return count == 1 && !char.IsControl(buffer[0]) ? buffer[0] : TypedChar.None;
        }
        catch
        {
            return TypingEchoHandler.VkCodeToChar(key.VkCode, key.Modifiers, key.CapsLockOn);
        }
    }
}
