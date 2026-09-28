namespace Vox.Core.Input;

/// <summary>
/// Detects the screen reader key pressed twice quickly on its own (NVDA's "press it twice to use
/// the key's own function"): the second press is passed to the system, so Caps Lock can still be
/// toggled and Insert can still switch overwrite mode. Used on the keyboard hook thread; only
/// arithmetic and flags, well within the hook's time budget.
/// </summary>
public sealed class ModifierTapDetector
{
    /// <summary>Longest gap between the end of the first tap and the second press.</summary>
    public const int DoubleTapMs = 500;

    private bool _downClean;      // modifier is down and no other key has been pressed since
    private bool _tapPending;     // a clean tap (down + up, nothing else) just finished
    private long _tapEndedAt;

    /// <summary>
    /// A key-down of the modifier. Returns true when it completes a double tap: this press should
    /// reach the system.
    /// </summary>
    /// <param name="isRepeat">Auto-repeat while the key is held (ignored).</param>
    public bool OnModifierDown(long nowMs, bool isRepeat)
    {
        if (isRepeat)
            return false;

        if (_tapPending && nowMs - _tapEndedAt <= DoubleTapMs)
        {
            _tapPending = false;
            _downClean = false;
            return true;
        }

        _tapPending = false;
        _downClean = true;
        return false;
    }

    /// <summary>A key-up of the modifier.</summary>
    public void OnModifierUp(long nowMs)
    {
        if (_downClean)
        {
            _tapPending = true;
            _tapEndedAt = nowMs;
        }
        _downClean = false;
    }

    /// <summary>Any other key pressed: the modifier was used for a command, not tapped.</summary>
    public void OnOtherKeyDown()
    {
        _downClean = false;
        _tapPending = false;
    }

    /// <summary>Forgets everything (e.g. after a session switch).</summary>
    public void Reset() => OnOtherKeyDown();
}
