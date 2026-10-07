namespace Vox.Core.Input;

/// <summary>
/// Numeric keypad keys for keymap lookup. With Num Lock off the keypad sends the navigation keys
/// (Numpad8 is Up, Numpad5 is Clear, Numpad Enter is Enter) without the extended-key flag that
/// the dedicated keys carry, so the keymap can bind the keypad separately, as NVDA's desktop
/// layout does: those keys resolve as VK_NUMPAD0–9 and VK_DECIMAL, and Numpad Enter as
/// <see cref="NumpadEnter"/>. When a keypad key has no binding of its own it resolves as the key
/// it stands for, so an unbound keypad arrow still works as an arrow. With Num Lock on the keypad
/// types digits, which are never bound.
/// </summary>
public static class NumpadKeys
{
    public const int Numpad0 = 0x60;
    public const int Numpad1 = 0x61;
    public const int Numpad2 = 0x62;
    public const int Numpad3 = 0x63;
    public const int Numpad4 = 0x64;
    public const int Numpad5 = 0x65;
    public const int Numpad6 = 0x66;
    public const int Numpad7 = 0x67;
    public const int Numpad8 = 0x68;
    public const int Numpad9 = 0x69;
    public const int Multiply = 0x6A;
    public const int Subtract = 0x6D;
    public const int Decimal = 0x6E;
    public const int Divide = 0x6F;

    /// <summary>Keymap code for Numpad Enter (not a Windows virtual key; Windows reports VK_RETURN).</summary>
    public const int NumpadEnter = 0x10D;

    private const int VK_RETURN = 0x0D;

    /// <summary>
    /// Whether a key with this virtual key code and extended-key flag came from the keypad: the
    /// keypad's navigation keys lack the flag the dedicated keys have, and its Enter has the flag
    /// the main Enter lacks.
    /// </summary>
    public static bool IsKeypad(int vkCode, bool extendedFlag) =>
        vkCode == VK_RETURN ? extendedFlag : !extendedFlag && KeypadCode(vkCode) is not null;

    /// <summary>
    /// The codes to look the key up by, most specific first: the keypad code and then the key it
    /// stands for, or just the key's own code. Num Lock digits get no code (-1: never bound).
    /// </summary>
    public static (int Primary, int? Fallback) BindingCodes(int vkCode, bool isKeypad)
    {
        if (isKeypad)
        {
            if (vkCode == VK_RETURN)
                return (NumpadEnter, VK_RETURN);
            if (KeypadCode(vkCode) is { } code)
                return (code, vkCode);
        }
        // Num Lock digits and the decimal point: typing, never commands
        if (vkCode is >= Numpad0 and <= Numpad9 or Decimal)
            return (-1, null);
        return (vkCode, null);
    }

    private static int? KeypadCode(int vkCode) => vkCode switch
    {
        0x2D => Numpad0, // Insert
        0x23 => Numpad1, // End
        0x28 => Numpad2, // Down
        0x22 => Numpad3, // Page Down
        0x25 => Numpad4, // Left
        0x0C => Numpad5, // Clear
        0x27 => Numpad6, // Right
        0x24 => Numpad7, // Home
        0x26 => Numpad8, // Up
        0x21 => Numpad9, // Page Up
        0x2E => Decimal, // Delete
        _ => null,
    };
}
