using Microsoft.Extensions.Options;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Localization;

namespace Vox.Core.Speech;

/// <summary>
/// The speech on demand toggle: flips the <see cref="VoxSettings.SpeechOnDemand"/> setting (so a
/// profile can set it too) and says the new state, with a cue. The state is said as an on-demand
/// utterance, so it is heard whichever way the mode is going.
/// </summary>
public sealed class SpeechOnDemandCommands
{
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly Action<VoxSettings> _updateSettings;
    private readonly SpeechQueue _speechQueue;
    private readonly IAudioCuePlayer? _cues;

    public SpeechOnDemandCommands(IOptionsMonitor<VoxSettings> settings, Action<VoxSettings> updateSettings,
        SpeechQueue speechQueue, IAudioCuePlayer? cues = null)
    {
        _settings = settings;
        _updateSettings = updateSettings;
        _speechQueue = speechQueue;
        _cues = cues;
    }

    /// <summary>Runs <paramref name="command"/> if it is the speech on demand toggle; returns whether it was.</summary>
    public bool TryHandle(NavigationCommand command)
    {
        if (command != NavigationCommand.ToggleSpeechOnDemand)
            return false;
        Toggle();
        return true;
    }

    public void Toggle()
    {
        var settings = _settings.CurrentValue;
        bool on = !settings.SpeechOnDemand;
        _updateSettings(settings with { SpeechOnDemand = on });
        _cues?.Play(on ? "checked" : "not_checked");
        _speechQueue.Enqueue(new Utterance(VoxStrings.Get(on ? "App.SpeechOnDemandOn" : "App.SpeechOnDemandOff"), SpeechPriority.Interrupt)
        {
            OnDemand = true,
        });
    }
}
