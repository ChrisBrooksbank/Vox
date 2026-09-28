using Vox.Core.Configuration;

namespace Vox.Core.Input;

/// <summary>
/// Tracks modifier and Caps Lock state from the raw key stream seen by the low-level hook.
///
/// WH_KEYBOARD_LL reports left/right-specific virtual keys (VK_LSHIFT 0xA0 … VK_RMENU 0xA5),
/// and GetKeyState on the hook thread is not synchronised with global input, so the hook keeps
/// its own state instead. Not thread-safe: call only from the hook thread.
/// </summary>
public sealed class KeyStateTracker
{
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const int VK_CAPITAL = 0x14;
    public const int VK_INSERT = 0x2D;
    public const int VK_LSHIFT = 0xA0;
    public const int VK_RSHIFT = 0xA1;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_LMENU = 0xA4;
    public const int VK_RMENU = 0xA5;

    private bool _leftShift, _rightShift, _leftCtrl, _rightCtrl, _leftAlt, _rightAlt;
    private bool _screenReaderModifier;
    private bool _capsLockDown;
    private volatile int _screenReaderModifierVk = VK_INSERT;

    /// <summary>Which key acts as the screen reader modifier. Safe to set from any thread.</summary>
    public ModifierKey ScreenReaderModifier
    {
        get => _screenReaderModifierVk == VK_CAPITAL ? ModifierKey.CapsLock : ModifierKey.Insert;
        set => _screenReaderModifierVk = value == ModifierKey.CapsLock ? VK_CAPITAL : VK_INSERT;
    }

    /// <summary>Current Caps Lock toggle state (meaningless when CapsLock is the screen reader modifier).</summary>
    public bool CapsLockOn { get; private set; }

    /// <summary>Seeds the Caps Lock toggle state (e.g. from GetKeyState at install time).</summary>
    public void SetCapsLockState(bool on) => CapsLockOn = on;

    /// <summary>Modifiers currently held.</summary>
    public KeyModifiers Current
    {
        get
        {
            var m = KeyModifiers.None;
            if (_leftShift || _rightShift) m |= KeyModifiers.Shift;
            if (_leftCtrl || _rightCtrl) m |= KeyModifiers.Ctrl;
            if (_leftAlt || _rightAlt) m |= KeyModifiers.Alt;
            if (_screenReaderModifier) m |= KeyModifiers.Insert;
            return m;
        }
    }

    /// <summary>
    /// Updates state for a key transition and returns the modifiers that were held,
    /// not counting the key being processed (so pressing Ctrl alone reports no modifiers).
    /// </summary>
    public KeyModifiers Process(int vkCode, bool isKeyDown, out bool isScreenReaderModifier)
    {
        var before = Current;
        isScreenReaderModifier = vkCode == _screenReaderModifierVk;

        if (isScreenReaderModifier)
        {
            _screenReaderModifier = isKeyDown;
            return isKeyDown ? before : Current;
        }

        switch (vkCode)
        {
            case VK_LSHIFT:
            case VK_SHIFT: _leftShift = isKeyDown; break;
            case VK_RSHIFT: _rightShift = isKeyDown; break;
            case VK_LCONTROL:
            case VK_CONTROL: _leftCtrl = isKeyDown; break;
            case VK_RCONTROL: _rightCtrl = isKeyDown; break;
            case VK_LMENU:
            case VK_MENU: _leftAlt = isKeyDown; break;
            case VK_RMENU: _rightAlt = isKeyDown; break;
            case VK_CAPITAL:
                // Toggle on the up→down transition only (ignore auto-repeat)
                if (isKeyDown && !_capsLockDown)
                    CapsLockOn = !CapsLockOn;
                _capsLockDown = isKeyDown;
                break;
        }

        // Key-down: state before pressing; key-up: state after releasing
        return isKeyDown ? before : Current;
    }

    /// <summary>
    /// Clears any Shift/Ctrl/Alt the tracker believes is held but <paramref name="isPhysicallyDown"/>
    /// reports as released. Key-ups can be missed while the secure desktop (Ctrl+Alt+Del, UAC, lock
    /// screen) has input, which would otherwise leave a modifier stuck "down".
    /// </summary>
    public void Reconcile(Func<int, bool> isPhysicallyDown)
    {
        if (_leftShift && !isPhysicallyDown(VK_LSHIFT)) _leftShift = false;
        if (_rightShift && !isPhysicallyDown(VK_RSHIFT)) _rightShift = false;
        if (_leftCtrl && !isPhysicallyDown(VK_LCONTROL)) _leftCtrl = false;
        if (_rightCtrl && !isPhysicallyDown(VK_RCONTROL)) _rightCtrl = false;
        if (_leftAlt && !isPhysicallyDown(VK_LMENU)) _leftAlt = false;
        if (_rightAlt && !isPhysicallyDown(VK_RMENU)) _rightAlt = false;
    }

    /// <summary>Forgets all held keys (e.g. after a session switch).</summary>
    public void Reset()
    {
        _leftShift = _rightShift = _leftCtrl = _rightCtrl = _leftAlt = _rightAlt = false;
        _screenReaderModifier = false;
        _capsLockDown = false;
    }

    /// <summary>True for any Shift/Ctrl/Alt virtual key (generic or left/right specific).</summary>
    public static bool IsModifierKey(int vkCode) =>
        vkCode is VK_SHIFT or VK_CONTROL or VK_MENU
            or VK_LSHIFT or VK_RSHIFT or VK_LCONTROL or VK_RCONTROL or VK_LMENU or VK_RMENU;
}
