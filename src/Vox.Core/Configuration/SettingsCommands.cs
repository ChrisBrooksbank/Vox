using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.Configuration;

/// <summary>
/// Opens the settings dialog (Insert+Ctrl+G). While it is open browse keys are off (it is
/// typed in like any dialog); it reports back with <see cref="VoxDialogClosedEvent"/>.
/// Pipeline thread.
/// </summary>
public sealed class SettingsCommands(
    ISettingsDialogPresenter presenter,
    IOptionsMonitor<VoxSettings> settings,
    Action<VoxSettings> updateSettings,
    SpeechEngineRegistry speech,
    BrowseModeController browse,
    IEventSink pipeline,
    ILogger<SettingsCommands> logger)
{
    public bool TryHandle(NavigationCommand command)
    {
        if (command != NavigationCommand.OpenSettings)
            return false;
        if (!browse.BeginOwnDialog())
            return true; // another Vox dialog is open
        _ = ShowAsync();
        return true;
    }

    private async Task ShowAsync()
    {
        try
        {
            var speechOptions = new SpeechOptions(speech.Engines, speech.GetAvailableVoices(), speech.MaxRateWpm, speech.Capabilities);
            var audioOptions = new AudioOptions(OutputDevices(), EarconSchemes());
            await presenter.ShowAsync(settings.CurrentValue, SettingsPages.All(speechOptions, audioOptions), updateSettings).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Settings dialog failed");
        }
        pipeline.Post(new VoxDialogClosedEvent(DateTimeOffset.UtcNow));
    }

    private IReadOnlyList<string> OutputDevices()
    {
        try { return Audio.AudioOutputDevices.List(); }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not list the audio output devices");
            return [];
        }
    }

    /// <summary>The earcon schemes installed: folders under assets/sounds with a manifest.</summary>
    private static IReadOnlyList<string> EarconSchemes()
    {
        var sounds = Path.Combine(AppContext.BaseDirectory, "assets", "sounds");
        if (!Directory.Exists(sounds))
            return ["default"];
        return Directory.GetDirectories(sounds)
            .Where(d => File.Exists(Path.Combine(d, "manifest.json")))
            .Select(d => Path.GetFileName(d)!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
