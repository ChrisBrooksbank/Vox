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
    private volatile bool _escapeGoesToPage;

    // Resolution contexts carried in KeyDecision.Context (0 = no decision)
    private const int BrowseContext = 1;
    private const int FocusContext = 2;
    private const int OutsideDocumentContext = 3;

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
    /// Set while the focused control has an open popup (expanded combo box, menu), so Escape
    /// reaches the page to close it instead of leaving Focus mode.
    /// </summary>
    public void SetEscapeGoesToPage(bool value)
    {
        _escapeGoesToPage = value;
    }

    /// <summary>
    /// The mode used for keymap resolution: the current mode inside a web document, Focus elsewhere.
    /// Outside documents only "Any" bindings apply.
    /// </summary>
    public InteractionMode EffectiveMode => _documentActive ? _currentMode : InteractionMode.Focus;

    private int CurrentContext =>
        !_documentActive ? OutsideDocumentContext
        : _currentMode == InteractionMode.Browse ? BrowseContext
        : FocusContext;

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

        var context = CurrentContext;
        bool found = TryResolve(evt, context, out _, out var passThrough);
        bool suppress = found && !passThrough;

        if (!found)
        {
            // Keys pressed with the screen reader modifier are Vox's, bound or not (NVDA behaviour)
            if ((evt.Modifiers & KeyModifiers.Insert) != 0)
                suppress = true;
            // In Browse mode, unbound typing keys must not reach the page, where they could fire
            // single-key site shortcuts (Gmail "e" archives, YouTube "m" mutes, ...)
            else if (context == BrowseContext && IsTypingKey(evt.VkCode)
                     && (evt.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) == 0)
                suppress = true;
        }

        return new KeyDecision(suppress, context);
    }

    /// <summary>True when <see cref="Decide"/> would swallow the key.</summary>
    public bool ShouldSuppress(KeyEvent evt) => Decide(evt).Suppress;

    /// <summary>Letters, digits, punctuation (OEM) keys, Space and the numeric keypad.</summary>
    public static bool IsTypingKey(int vk) =>
        vk == 0x20
        || vk is >= 0x30 and <= 0x39
        || vk is >= 0x41 and <= 0x5A
        || vk is >= 0x60 and <= 0x6F
        || vk is >= 0xBA and <= 0xC0
        || vk is >= 0xDB and <= 0xDF
        || vk == 0xE2;

    private bool TryResolve(KeyEvent evt, int context, out NavigationCommand command, out bool passThrough)
    {
        bool found = context == OutsideDocumentContext
            ? _keyMap.TryResolveOutsideDocument(evt.Modifiers, evt.VkCode, out command, out passThrough)
            : _keyMap.TryResolve(evt.Modifiers, evt.VkCode,
                context == BrowseContext ? InteractionMode.Browse : InteractionMode.Focus,
                out command, out passThrough);

        // Escape closes an open popup rather than leaving Focus mode
        if (found && command == NavigationCommand.ExitFocusMode && _escapeGoesToPage)
            found = false;

        return found;
    }

    private void OnKeyPressed(object? sender, KeyEvent evt)
    {
        var slot = evt.VkCode & 0xFF;

        if (evt.IsKeyDown)
        {
            // Resolve in the context the hook used when the key was pressed, so the swallow
            // decision and the command always agree even if the mode changed since
            var context = evt.Decision.Context > 0 ? evt.Decision.Context : CurrentContext;

            if (TryResolve(evt, context, out var command, out _))
            {
                _logger.LogDebug(
                    "Key {VkCode} with {Modifiers} in context {Context} -> {Command}",
                    evt.VkCode, evt.Modifiers, context, command);

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
