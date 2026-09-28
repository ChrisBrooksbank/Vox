namespace Vox.Core.Input;

/// <summary>
/// Modifier keys held when a key event occurred.
/// <see cref="Insert"/> means the screen reader modifier is held — Insert or CapsLock,
/// depending on the <c>ModifierKey</c> setting.
/// </summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1 << 0,
    Ctrl = 1 << 1,
    Alt = 1 << 2,
    Insert = 1 << 3
}

public readonly struct KeyEvent
{
    public int VkCode { get; init; }
    public KeyModifiers Modifiers { get; init; }
    public bool IsKeyDown { get; init; }
    public long Timestamp { get; init; }

    /// <summary>True when Caps Lock is toggled on (and CapsLock is not the screen reader modifier).</summary>
    public bool CapsLockOn { get; init; }
}
