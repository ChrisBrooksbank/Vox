using Microsoft.Extensions.Logging;
using Vox.Core.Pipeline;

namespace Vox.Core.Input;

/// <summary>
/// Subscribes to IKeyboardHook.KeyPressed, looks up the current keymap, and dispatches
/// either a NavigationCommandEvent (when a mapping is found) or a RawKeyEvent (for typing
/// echo and pass-through) to the EventPipeline.
///
/// Also tracks InteractionMode so keymap lookup uses the correct mode.
/// The mode is updated by calling SetMode (typically driven by ModeChangedEvent handling).
/// </summary>
public sealed class KeyInputDispatcher
{
    private readonly IKeyboardHook _hook;
    private readonly KeyMap _keyMap;
    private readonly IEventSink _pipeline;
    private readonly ILogger<KeyInputDispatcher> _logger;

    private volatile InteractionMode _currentMode = InteractionMode.Browse;

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
        _logger.LogDebug("KeyInputDispatcher started");
    }

    /// <summary>
    /// Stops listening to keyboard events.
    /// </summary>
    public void Stop()
    {
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

    private void OnKeyPressed(object? sender, KeyEvent evt)
    {
        // Only dispatch on key-down events; key-up events are passed through as RawKeyEvent
        // so TypingEchoHandler can process them.
        if (evt.IsKeyDown)
        {
            var mode = _currentMode;
            if (_keyMap.TryResolve(evt.Modifiers, evt.VkCode, mode, out var command))
            {
                _logger.LogDebug(
                    "Key {VkCode} with {Modifiers} in {Mode} -> {Command}",
                    evt.VkCode, evt.Modifiers, mode, command);

                _pipeline.Post(new NavigationCommandEvent(DateTimeOffset.UtcNow, command));
                return;
            }
        }

        // No command mapping found (or key-up): forward as RawKeyEvent for typing echo etc.
        _pipeline.Post(new RawKeyEvent(DateTimeOffset.UtcNow, evt));
    }
}
