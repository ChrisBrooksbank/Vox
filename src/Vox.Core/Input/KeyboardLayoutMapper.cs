using System.Runtime.InteropServices;
using System.Text;

namespace Vox.Core.Input;

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
    /// The character typed by <paramref name="key"/> in the foreground window's layout, or '\0'
    /// when it types nothing (or is a dead key). Falls back to the US table off Windows.
    /// </summary>
    public static char ToChar(KeyEvent key)
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
            // Ctrl/Alt presses are shortcuts and never reach here (AltGr is Ctrl+Alt: not echoed)

            var layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out _));
            var scanCode = key.ScanCode != 0 ? (uint)key.ScanCode : MapVirtualKey((uint)key.VkCode, MAPVK_VK_TO_VSC);
            var buffer = new StringBuilder(4);
            int count = ToUnicodeEx((uint)key.VkCode, scanCode, state, buffer, buffer.Capacity, DontChangeKeyboardState, layout);
            return count == 1 && !char.IsControl(buffer[0]) ? buffer[0] : '\0';
        }
        catch
        {
            return TypingEchoHandler.VkCodeToChar(key.VkCode, key.Modifiers, key.CapsLockOn);
        }
    }
}
