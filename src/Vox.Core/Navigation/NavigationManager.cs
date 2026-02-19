using Microsoft.Extensions.Logging;
using Vox.Core.Audio;
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
///   - Enter pressed on an edit field while in Browse mode -> switch to Focus mode + play focus_mode.wav
///   - Focus leaves a form field (FocusChangedEvent to non-form element) while in Focus mode -> switch to Browse mode + play browse_mode.wav
/// </summary>
public sealed class NavigationManager
{
    private readonly IAudioCuePlayer _audioCuePlayer;
    private readonly IEventSink _pipeline;
    private readonly ILogger<NavigationManager> _logger;

    private InteractionMode _currentMode = InteractionMode.Browse;

    public NavigationManager(
        IAudioCuePlayer audioCuePlayer,
        IEventSink pipeline,
        ILogger<NavigationManager> logger)
    {
        _audioCuePlayer = audioCuePlayer;
        _pipeline = pipeline;
        _logger = logger;
    }

    /// <summary>Current interaction mode.</summary>
    public InteractionMode CurrentMode => _currentMode;

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

            case NavigationCommand.ActivateElement when _currentMode == InteractionMode.Browse:
                // Auto-switch to Focus mode if activating an edit field
                if (IsEditField(currentNode))
                {
                    SwitchTo(InteractionMode.Focus, "activated edit field");
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
    /// When focus moves to a non-form element while in Focus mode, auto-switch to Browse mode.
    /// </summary>
    public void HandleFocusChanged(FocusChangedEvent evt)
    {
        if (_currentMode == InteractionMode.Focus && !IsFormFieldControlType(evt.ControlType))
        {
            SwitchTo(InteractionMode.Browse, "focus left form field");
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
    /// Switches to the specified mode, posting a ModeChangedEvent and playing the appropriate audio cue.
    /// No-op if already in the requested mode.
    /// </summary>
    public void SwitchTo(InteractionMode mode, string? reason = null)
    {
        if (_currentMode == mode) return;

        _currentMode = mode;
        _logger.LogInformation("Mode changed to {Mode} ({Reason})", mode, reason ?? "unknown");

        var cueName = mode == InteractionMode.Focus ? "focus_mode" : "browse_mode";
        _audioCuePlayer.Play(cueName);

        _pipeline.Post(new ModeChangedEvent(DateTimeOffset.UtcNow, mode, reason));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool IsEditField(VBufferNode? node)
    {
        if (node is null) return false;
        return IsFormFieldControlType(node.ControlType) || node.IsFocusable;
    }

    private static bool IsFormFieldControlType(string controlType) =>
        controlType is "Edit" or "ComboBox" or "CheckBox" or "RadioButton"
                    or "Spinner" or "Slider" or "List" or "ListItem";
}
