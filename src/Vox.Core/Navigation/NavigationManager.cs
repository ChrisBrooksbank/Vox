using Microsoft.Extensions.Logging;
using Vox.Core.Buffer;
using Vox.Core.Input;
using Vox.Core.Pipeline;

namespace Vox.Core.Navigation;

/// <summary>
/// Browse/Focus mode state machine.
///
/// Browse mode: single-letter navigation keys are consumed by Vox (via QuickNavHandler).
/// Focus mode: all keys pass through to the application except Insert+Space (ToggleMode).
///
/// Auto-switch rules:
///   - Enter pressed on an edit field while in Browse mode -> switch to Focus mode
///   - Focus leaves a form field (FocusChangedEvent to non-form element) while in Focus mode -> switch to Browse mode
///
/// Each switch posts a ModeChangedEvent; the EventPipeline plays the mode cue and announces the mode.
/// </summary>
public sealed class NavigationManager
{
    private readonly IEventSink _pipeline;
    private readonly ILogger<NavigationManager> _logger;

    private InteractionMode _currentMode = InteractionMode.Browse;

    public NavigationManager(
        IEventSink pipeline,
        ILogger<NavigationManager> logger)
    {
        _pipeline = pipeline;
        _logger = logger;
    }

    /// <summary>Current interaction mode.</summary>
    public InteractionMode CurrentMode => _currentMode;

    /// <summary>Raised after the mode changes (used to keep key resolution in sync).</summary>
    public event EventHandler<InteractionMode>? ModeChanged;

    /// <summary>
    /// Processes a NavigationCommandEvent. Returns true if the command was handled
    /// (and should not be forwarded further), false if it should be passed through.
    /// </summary>
    public bool HandleCommand(NavigationCommand command, VBufferNode? currentNode)
    {
        switch (command)
        {
            case NavigationCommand.ToggleMode:
                ToggleMode();
                return true;

            case NavigationCommand.ExitFocusMode:
                SwitchTo(InteractionMode.Browse, "escape");
                return true;

            case NavigationCommand.ActivateElement when _currentMode == InteractionMode.Browse:
                // Auto-switch to Focus mode if activating an edit field
                if (IsEditField(currentNode))
                {
                    SwitchTo(InteractionMode.Focus, "activated edit field", announce: false);
                }
                return false; // Let the activation proceed

            default:
                // In Focus mode, block all navigation commands except ToggleMode (handled above)
                if (_currentMode == InteractionMode.Focus)
                {
                    _logger.LogDebug("Blocked command {Command} in Focus mode", command);
                    return true; // Swallow: pass-through keys are handled by key suppression logic
                }
                return false;
        }
    }

    /// <summary>
    /// Processes a FocusChangedEvent for auto-mode-switching.
    /// When focus moves to an element that doesn't need Focus mode (a link, button, check box or
    /// radio button) while in Focus mode, auto-switch to Browse mode, as NVDA does.
    /// </summary>
    public void HandleFocusChanged(FocusChangedEvent evt)
    {
        if (_currentMode == InteractionMode.Focus && !FormControls.NeedsFocusMode(evt.ControlType, evt.AriaRole))
        {
            SwitchTo(InteractionMode.Browse, "focus left edit field", announce: false);
        }
    }

    /// <summary>
    /// Toggles between Browse and Focus mode.
    /// </summary>
    public void ToggleMode()
    {
        var next = _currentMode == InteractionMode.Browse
            ? InteractionMode.Focus
            : InteractionMode.Browse;
        SwitchTo(next, "user toggled");
    }

    /// <summary>
    /// Switches to the specified mode and posts a ModeChangedEvent (the pipeline plays the cue).
    /// No-op if already in the requested mode.
    /// </summary>
    /// <param name="announce">
    /// True to speak the new mode (user toggles); false for automatic switches, which only play
    /// the cue so they don't talk over the field announcement that caused them.
    /// </param>
    public void SwitchTo(InteractionMode mode, string? reason = null, bool announce = true)
    {
        if (_currentMode == mode) return;

        _currentMode = mode;
        _logger.LogInformation("Mode changed to {Mode} ({Reason})", mode, reason ?? "unknown");

        ModeChanged?.Invoke(this, mode);
        _pipeline.Post(new ModeChangedEvent(DateTimeOffset.UtcNow, mode, reason, announce));
    }

    /// <summary>
    /// Sets the mode without announcing it (e.g. when a new document gets focus).
    /// Still raises <see cref="ModeChanged"/> so key handling stays in sync.
    /// </summary>
    public void ResetMode(InteractionMode mode)
    {
        if (_currentMode == mode) return;

        _currentMode = mode;
        _logger.LogDebug("Mode reset to {Mode}", mode);
        ModeChanged?.Invoke(this, mode);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// True for controls that need keys passed through to be used (text entry, arrow-key controls).
    /// Links, buttons, checkboxes etc. are activated from Browse mode without switching.
    /// </summary>
    public static bool IsEditField(VBufferNode? node)
    {
        if (node is null) return false;
        return FormControls.NeedsFocusMode(node.ControlType, node.AriaRole);
    }
}
