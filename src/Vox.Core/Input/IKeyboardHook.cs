using Vox.Core.Configuration;

namespace Vox.Core.Input;

public interface IKeyboardHook
{
    event EventHandler<KeyEvent> KeyPressed;
    void Install();
    void Uninstall();

    /// <summary>
    /// Called on the hook thread for every key-down; return a decision with Suppress = true to stop
    /// the key reaching other applications. The matching key-up is swallowed automatically.
    /// The decision is attached to the key's events (<see cref="KeyEvent.Decision"/>).
    /// Must be fast (a dictionary lookup at most) and must not throw or block.
    /// </summary>
    Func<KeyEvent, KeyDecision>? SuppressionFilter { get; set; }

    /// <summary>
    /// Which physical key acts as the screen reader modifier (reported as <see cref="KeyModifiers.Insert"/>).
    /// The modifier key itself is always swallowed.
    /// </summary>
    ModifierKey ScreenReaderModifier { get; set; }
}
