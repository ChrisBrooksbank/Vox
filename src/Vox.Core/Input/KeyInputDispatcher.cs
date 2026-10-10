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
    // Replaced whole when the layout changes; read on the hook thread
    private volatile KeyMap _keyMap;
    private readonly IEventSink _pipeline;
    private readonly ILogger<KeyInputDispatcher> _logger;

    private volatile InteractionMode _currentMode = InteractionMode.Browse;
    private volatile bool _documentActive;
    private volatile bool _escapeGoesToPage;

    // Resolution contexts carried in KeyDecision.Context (0 = no decision). The low bits hold the
    // context; EscapeToPageFlag records whether Escape belonged to the page at press time.
    private const int BrowseContext = 1;
    private const int FocusContext = 2;
    private const int OutsideDocumentContext = 3;
    private const int ContextMask = 0x3;
    private const int EscapeToPageFlag = 0x4;
    // The focused application was in sleep mode when the key was pressed
    private const int SleepFlag = 0x8;
    // Input help was on when the key was pressed
    private const int HelpFlag = 0x10;

    private volatile bool _inputHelp;

    /// <summary>
    /// Input help (Insert+1): keys are described instead of run, and none reaches the application
    /// except modifiers alone. Turned on and off by <see cref="NavigationCommand.ToggleInputHelp"/>
    /// itself, on the dispatcher thread, so the next key is already treated the new way.
    /// </summary>
    public bool IsInputHelpOn
    {
        get => _inputHelp;
        set => _inputHelp = value;
    }

    // Keys whose key-down was dispatched as a command; their key-ups are dropped so a key that
    // changed the mode (e.g. Enter entering Focus mode) is not then echoed as typing.
    // Only touched on the hook consumer thread.
    private readonly bool[] _commandKeysDown = new bool[256];

    public KeyInputDispatcher(
        IKeyboardHook hook,
        KeyMap keyMap,
        IEventSink pipeline,
        ILogger<KeyInputDispatcher> logger,
        Diagnostics.LatencyTracker? latency = null)
    {
        _latency = latency;
        _hook = hook;
        _keyMap = keyMap;
        _pipeline = pipeline;
        _logger = logger;
    }

    /// <summary>
    /// Whether the focused application is in sleep mode: then keys reach it untouched and only
    /// <see cref="NavigationCommand.ToggleSleepMode"/> is Vox's. Called on the hook thread, so it
    /// must only read a cached value.
    /// </summary>
    public Func<bool>? IsAsleep { get; set; }

    private bool AsleepNow()
    {
        try { return IsAsleep?.Invoke() == true; }
        catch { return false; }
    }

    /// <summary>Switches to another keymap (another keyboard layout); takes effect with the next key.</summary>
    public void SetKeyMap(KeyMap keyMap) => _keyMap = keyMap;

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
        bool escapeToPage = _escapeGoesToPage;
        bool found = TryResolve(evt, context, escapeToPage, out var command, out var passThrough);
        int flags = context | (escapeToPage ? EscapeToPageFlag : 0);

        // Asleep: everything goes to the application except the key that wakes Vox
        if (AsleepNow())
            return new KeyDecision(found && command == NavigationCommand.ToggleSleepMode && !passThrough, flags | SleepFlag);

        // Input help: keys are described, not run or typed (a modifier alone still goes through)
        if (_inputHelp)
            return new KeyDecision(!InputHelp.IsModifierKey(evt.VkCode), flags | HelpFlag);

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

        return new KeyDecision(suppress, flags);
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

    private bool TryResolve(KeyEvent evt, int context, bool escapeToPage,
        out NavigationCommand command, out bool passThrough)
    {
        // Keypad keys resolve by their own binding first, then as the key they stand for
        var (primary, fallback) = NumpadKeys.BindingCodes(evt.VkCode, evt.IsKeypad);
        bool found = Lookup(evt.Modifiers, primary, context, out command, out passThrough)
            || (fallback is { } code && Lookup(evt.Modifiers, code, context, out command, out passThrough));

        // Escape closes an open popup rather than leaving Focus mode
        if (found && command == NavigationCommand.ExitFocusMode && escapeToPage)
            found = false;

        return found;
    }

    private bool Lookup(KeyModifiers modifiers, int vkCode, int context, out NavigationCommand command, out bool passThrough)
    {
        if (vkCode < 0)
        {
            command = default;
            passThrough = false;
            return false;
        }
        return context == OutsideDocumentContext
            ? _keyMap.TryResolveOutsideDocument(modifiers, vkCode, out command, out passThrough)
            : _keyMap.TryResolve(modifiers, vkCode,
                context == BrowseContext ? InteractionMode.Browse : InteractionMode.Focus,
                out command, out passThrough);
    }

    private readonly Diagnostics.LatencyTracker? _latency;

    private void OnKeyPressed(object? sender, KeyEvent evt)
    {
        var slot = evt.VkCode & 0xFF;

        if (evt.IsKeyDown && _latency is not null)
        {
            // The hook's time is GetTickCount's: compare with the same 32-bit clock
            uint elapsed = unchecked((uint)Environment.TickCount - (uint)evt.Timestamp);
            if (evt.Timestamp != 0 && elapsed < 60_000)
                _latency.Record(Diagnostics.LatencyTracker.HookToDispatcher, TimeSpan.FromMilliseconds(elapsed));
            _latency.NoteKeyPress();
        }

        if (evt.IsKeyDown)
        {
            // Resolve exactly as the hook did when the key was pressed (context and whether Escape
            // belonged to the page), so the swallow decision and the command always agree even if
            // the mode or a popup changed since
            bool decided = evt.Decision.Context > 0;
            var context = decided ? evt.Decision.Context & ContextMask : CurrentContext;
            bool escapeToPage = decided ? (evt.Decision.Context & EscapeToPageFlag) != 0 : _escapeGoesToPage;
            bool asleep = decided ? (evt.Decision.Context & SleepFlag) != 0 : AsleepNow();
            bool help = decided ? (evt.Decision.Context & HelpFlag) != 0 : _inputHelp;

            if (help)
            {
                bool bound = TryResolve(evt, context, escapeToPage, out var described, out _);
                if (bound && described == NavigationCommand.ToggleInputHelp)
                {
                    _inputHelp = false;
                    _commandKeysDown[slot] = true;
                    _pipeline.Post(new NavigationCommandEvent(DateTimeOffset.UtcNow, described));
                }
                else if (!InputHelp.IsModifierKey(evt.VkCode))
                {
                    _commandKeysDown[slot] = true;
                    _pipeline.Post(new InputHelpEvent(DateTimeOffset.UtcNow, bound ? described : null, evt.Modifiers, evt.VkCode, evt.IsKeypad));
                }
                return;
            }

            if (TryResolve(evt, context, escapeToPage, out var command, out _)
                && (!asleep || command == NavigationCommand.ToggleSleepMode))
            {
                _logger.LogDebug(
                    "Key {VkCode} with {Modifiers} in context {Context} -> {Command}",
                    evt.VkCode, evt.Modifiers, context, command);

                if (command == NavigationCommand.ToggleInputHelp)
                    _inputHelp = true;
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
