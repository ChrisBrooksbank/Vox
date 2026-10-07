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

    /// <summary>Hardware scan code (used to map the key through the active keyboard layout).</summary>
    public int ScanCode { get; init; }

    /// <summary>
    /// A navigation key or Enter pressed on the numeric keypad (Num Lock off) rather than the
    /// dedicated key with the same virtual key code. See <see cref="NumpadKeys"/>.
    /// </summary>
    public bool IsKeypad { get; init; }

    /// <summary>True when Caps Lock is toggled on (and CapsLock is not the screen reader modifier).</summary>
    public bool CapsLockOn { get; init; }

    /// <summary>
    /// The suppression filter's decision for this key, made on the hook thread when the key was pressed
    /// (for key-ups: the decision made for the matching key-down).
    /// </summary>
    public KeyDecision Decision { get; init; }
}

/// <summary>
/// Result of <see cref="IKeyboardHook.SuppressionFilter"/>: whether to swallow the key, plus an opaque
/// <paramref name="Context"/> the filter's owner can use to act on the key later exactly as decided
/// at press time (e.g. the interaction mode used to resolve it). 0 means "no context".
/// </summary>
public readonly record struct KeyDecision(bool Suppress, int Context = 0)
{
    public static KeyDecision Pass => default;
    public static KeyDecision Swallow => new(true);
}
