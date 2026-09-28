using Microsoft.Extensions.Logging;
using Vox.Core.Pipeline;

namespace Vox.Core.Input;

/// <summary>
/// Subscribes to IKeyboardHook.KeyPressed, looks up the current keymap, and dispatches
/// either a NavigationCommandEvent (when a mapping is found) or a RawKeyEvent (for typing
/// echo and pass-through) to the EventPipeline.
///
/// Also tracks InteractionMode so keymap lookup uses the correct mode.
/// The mode is updated by calling SetMode (driven by NavigationManager mode changes).
/// Browse mode only applies while a web document is active (<see cref="SetDocumentActive"/>);
/// everywhere else keys resolve as in Focus mode so typing in ordinary apps is untouched.
///
/// Installs <see cref="ShouldSuppress"/> as the hook's suppression filter so bound keys
/// are consumed by Vox instead of also reaching the application.
/// </summary>
public sealed class KeyInputDispatcher
{
    private readonly IKeyboardHook _hook;
    private readonly KeyMap _keyMap;
    private readonly IEventSink _pipeline;
    private readonly ILogger<KeyInputDispatcher> _logger;

    private volatile InteractionMode _currentMode = InteractionMode.Browse;
    private volatile bool _documentActive;

    // Keys whose key-down was dispatched as a command; their key-ups are dropped so a key that
    // changed the mode (e.g. Enter entering Focus mode) is not then echoed as typing.
    // Only touched on the hook consumer thread.
    private readonly bool[] _commandKeysDown = new bool[256];

    public KeyInputDispatcher(
        IKeyboardHook hook,
        KeyMap keyMap,
        IEventSink pipeline,
        ILogger<KeyInputDispatcher> logger)
    {
        _hook = hook;
        _keyMap = keyMap;
        _pipeline = pipeline;
        _logger = logger;
    }

    /// <summary>
    /// Starts listening to keyboard events by subscribing to the hook.
    /// </summary>
    public void Start()
    {
        _hook.KeyPressed += OnKeyPressed;
        _hook.SuppressionFilter = Decide;
        _logger.LogDebug("KeyInputDispatcher started");
    }

    /// <summary>
    /// Stops listening to keyboard events.
    /// </summary>
    public void Stop()
    {
        _hook.SuppressionFilter = null;
        _hook.KeyPressed -= OnKeyPressed;
        _logger.LogDebug("KeyInputDispatcher stopped");
    }

    /// <summary>
    /// Updates the current interaction mode used for keymap resolution.
    /// Should be called when a ModeChangedEvent is processed.
    /// </summary>
    public void SetMode(InteractionMode mode)
    {
        _currentMode = mode;
    }

    /// <summary>
    /// Gets the current interaction mode.
    /// </summary>
    public InteractionMode CurrentMode => _currentMode;

    /// <summary>
    /// Sets whether a web document (virtual buffer) currently has focus.
    /// </summary>
    public void SetDocumentActive(bool active)
    {
        _documentActive = active;
    }

    /// <summary>
    /// The mode used for keymap resolution: the current mode inside a web document, Focus elsewhere.
    /// </summary>
    public InteractionMode EffectiveMode => _documentActive ? _currentMode : InteractionMode.Focus;

    /// <summary>
    /// Suppression filter run on the keyboard hook thread for each key-down.
    /// Swallows keys bound to a command (unless marked passThrough) and records the mode used,
    /// so the command is later resolved exactly as it was when the key was pressed.
    /// Only a read-only dictionary lookup — safe for the &lt; 1ms hook budget.
    /// </summary>
    public KeyDecision Decide(KeyEvent evt)
    {
        if (!evt.IsKeyDown)
            return KeyDecision.Pass;

        var mode = EffectiveMode;
        bool suppress = _keyMap.TryResolve(evt.Modifiers, evt.VkCode, mode, out _, out var passThrough)
            && !passThrough;
        return new KeyDecision(suppress, EncodeMode(mode));
    }

    /// <summary>True when <see cref="Decide"/> would swallow the key.</summary>
    public bool ShouldSuppress(KeyEvent evt) => Decide(evt).Suppress;

    private static int EncodeMode(InteractionMode mode) => (int)mode + 1;

    private void OnKeyPressed(object? sender, KeyEvent evt)
    {
        var slot = evt.VkCode & 0xFF;

        if (evt.IsKeyDown)
        {
            // Resolve with the mode the hook used when the key was pressed, so the swallow
            // decision and the command always agree even if the mode changed since
            var mode = evt.Decision.Context > 0
                ? (InteractionMode)(evt.Decision.Context - 1)
                : EffectiveMode;

            if (_keyMap.TryResolve(evt.Modifiers, evt.VkCode, mode, out var command))
            {
                _logger.LogDebug(
                    "Key {VkCode} with {Modifiers} in {Mode} -> {Command}",
                    evt.VkCode, evt.Modifiers, mode, command);

                _commandKeysDown[slot] = true;
                _pipeline.Post(new NavigationCommandEvent(DateTimeOffset.UtcNow, command));
                return;
            }
        }
        else if (_commandKeysDown[slot])
        {
            // Key-up of a command key: not typing
            _commandKeysDown[slot] = false;
            return;
        }

        // No command mapping found (or key-up): forward as RawKeyEvent for typing echo etc.
        _pipeline.Post(new RawKeyEvent(DateTimeOffset.UtcNow, evt));
    }
}
