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
            var options = new SpeechOptions(speech.Engines, speech.GetAvailableVoices(), speech.MaxRateWpm, speech.Capabilities);
            await presenter.ShowAsync(settings.CurrentValue, SettingsPages.All(options), updateSettings).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Settings dialog failed");
        }
        pipeline.Post(new VoxDialogClosedEvent(DateTimeOffset.UtcNow));
    }
}
