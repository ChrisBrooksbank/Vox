using Microsoft.Extensions.Logging;
using Vox.Core.Accessibility;
using Vox.Core.Buffer;
using Vox.Core.Input;
using Vox.Core.Localization;
using Vox.Core.Navigation;

namespace Vox.Core.AppModules.Meetings;

/// <summary>The default capture device's mute, through Core Audio.</summary>
public interface IMicrophone
{
    /// <summary>Whether the default microphone is muted, or null when there is none or it can't be read.</summary>
    bool? IsMuted();

    /// <summary>Mutes or unmutes it; false when that failed.</summary>
    bool SetMuted(bool muted);
}

/// <summary>
/// ToggleMicrophone, whatever application has focus: in Teams and Zoom it presses the application's
/// own mute button, in Teams or Google Meet in a browser the page's, and otherwise it mutes the default
/// capture device. The new state is said. Runs on the pipeline thread.
/// </summary>
public sealed class MicrophoneCommands
{
    private static readonly string[] MeetingHosts = ["teams.microsoft.com", "teams.live.com", "teams.cloud.microsoft", "meet.google.com"];
    private static readonly string[] MeetingProcesses = ["ms-teams", "Teams", "Zoom"];

    private readonly IMicrophone _microphone;
    private readonly IWindowButtonPresser _buttons;
    private readonly Func<string?> _foregroundProcess;
    private readonly Func<VBufferDocument?> _activeDocument;
    private readonly Func<string?> _currentSite;
    private readonly Func<VBufferNode, Task<bool>> _activate;
    private readonly Action<string> _say;
    private readonly ILogger? _logger;

    public MicrophoneCommands(IMicrophone microphone, IWindowButtonPresser buttons, Func<string?> foregroundProcess,
        Func<VBufferDocument?> activeDocument, Func<string?> currentSite, Func<VBufferNode, Task<bool>> activate,
        Action<string> say, ILogger? logger = null)
    {
        _microphone = microphone;
        _buttons = buttons;
        _foregroundProcess = foregroundProcess;
        _activeDocument = activeDocument;
        _currentSite = currentSite;
        _activate = activate;
        _say = say;
        _logger = logger;
    }

    /// <summary>Runs <paramref name="command"/> if it is ToggleMicrophone; returns whether it was.</summary>
    public bool TryHandle(NavigationCommand command)
    {
        if (command != NavigationCommand.ToggleMicrophone)
            return false;
        _ = ToggleAsync();
        return true;
    }

    public async Task ToggleAsync()
    {
        try
        {
            bool? nowMuted = null;
            if (IsMeetingApp(_foregroundProcess()))
                nowMuted = await PressInWindowAsync().ConfigureAwait(false);
            else if (IsMeetingSite(_currentSite()) && _activeDocument() is { } document)
                nowMuted = await PressOnPageAsync(document).ConfigureAwait(false);
            nowMuted ??= ToggleDevice();
            _say(nowMuted switch
            {
                true => VoxStrings.Get("Meeting.MicrophoneMuted"),
                false => VoxStrings.Get("Meeting.MicrophoneOn"),
                null => VoxStrings.Get("Meeting.MicrophoneFailed"),
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "The microphone could not be toggled");
            _say(VoxStrings.Get("Meeting.MicrophoneFailed"));
        }
    }

    internal static bool IsMeetingApp(string? process) =>
        process is not null && MeetingProcesses.Contains(process, StringComparer.OrdinalIgnoreCase);

    internal static bool IsMeetingSite(string? site)
    {
        if (site is null || !Uri.TryCreate(site.Contains("://") ? site : "https://" + site, UriKind.Absolute, out var uri))
            return false;
        return MeetingHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase));
    }

    // Returns the microphone's state after the press, or null when the window had no mute button
    private async Task<bool?> PressInWindowAsync()
    {
        bool? mutes = null;
        bool pressed = await _buttons.PressButtonAsync(name =>
        {
            mutes = MeetingControls.PressingMutes(name);
            return mutes is not null;
        }).ConfigureAwait(false);
        return pressed ? mutes : null;
    }

    private async Task<bool?> PressOnPageAsync(VBufferDocument document)
    {
        foreach (var button in document.Buttons)
        {
            if (MeetingControls.PressingMutes(button.Name) is not { } mutes)
                continue;
            return await _activate(button).ConfigureAwait(false) ? mutes : null;
        }
        return null;
    }

    private bool? ToggleDevice()
    {
        if (_microphone.IsMuted() is not { } muted)
            return null;
        return _microphone.SetMuted(!muted) ? !muted : null;
    }
}
