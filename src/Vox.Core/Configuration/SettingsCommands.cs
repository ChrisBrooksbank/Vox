using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.Configuration;

/// <summary>Where the input gestures dialog reads the keys from and saves them to.</summary>
/// <param name="ConfigDirectory">The standard keymaps (assets/config).</param>
/// <param name="UserKeyMapPath">The user keymap; null where it may not be written (secure screens).</param>
/// <param name="Dispatcher">Captures the new keys and gets the reloaded keymap.</param>
/// <param name="ReloadKeyMap">Loads the keymap again after a save.</param>
public sealed record GestureSetup(
    IInputGesturesPresenter Presenter,
    string ConfigDirectory,
    string? UserKeyMapPath,
    KeyInputDispatcher Dispatcher,
    Action ReloadKeyMap);

/// <summary>
/// Opens Vox's settings dialog (Insert+Ctrl+G) and input gestures dialog. While one is open
/// browse keys are off (it is typed in like any dialog); it reports back with
/// <see cref="VoxDialogClosedEvent"/>. Pipeline thread.
/// </summary>
public sealed class SettingsCommands(
    ISettingsDialogPresenter presenter,
    IOptionsMonitor<VoxSettings> settings,
    Action<VoxSettings> updateSettings,
    SpeechEngineRegistry speech,
    BrowseModeController browse,
    IEventSink pipeline,
    ILogger<SettingsCommands> logger,
    SpeechQueue? speechQueue = null,
    GestureSetup? gestures = null)
{
    public bool TryHandle(NavigationCommand command)
    {
        switch (command)
        {
            case NavigationCommand.OpenSettings:
                if (browse.BeginOwnDialog())
                    _ = ShowSettingsAsync();
                return true;
            case NavigationCommand.OpenInputGestures:
                if (gestures?.UserKeyMapPath is null)
                    Say("Keys can't be changed here");
                else if (browse.BeginOwnDialog())
                    _ = ShowGesturesAsync(gestures);
                return true;
            default:
                return false;
        }
    }

    private async Task ShowSettingsAsync()
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

    private async Task ShowGesturesAsync(GestureSetup setup)
    {
        try
        {
            var path = setup.UserKeyMapPath!;
            var layout = KeyMap.LayoutBindings(setup.ConfigDirectory, settings.CurrentValue.KeyboardLayout);
            var editor = new GestureEditor(layout, ReadUserKeyMap(path));
            var services = new GestureDialogServices(
                setup.Dispatcher.CaptureNextKey, setup.Dispatcher.CancelCapture, Say,
                settings.CurrentValue.ModifierKey == ModifierKey.CapsLock ? "Caps Lock" : "Insert");
            if (await setup.Presenter.ShowAsync(editor, services).ConfigureAwait(false))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temp = path + ".tmp";
                await File.WriteAllTextAsync(temp, KeyMap.ToJson(editor.UserBindings())).ConfigureAwait(false);
                File.Move(temp, path, overwrite: true);
                setup.ReloadKeyMap();
                Say("Keys saved");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Input gestures dialog failed");
            Say("Keys could not be saved");
        }
        pipeline.Post(new VoxDialogClosedEvent(DateTimeOffset.UtcNow));
    }

    private IReadOnlyList<KeyBinding> ReadUserKeyMap(string path)
    {
        try
        {
            return File.Exists(path) ? KeyMap.ParseBindings(File.ReadAllText(path)) : [];
        }
        catch (Exception ex)
        {
            // An unreadable file is replaced by what the dialog saves
            logger.LogWarning(ex, "Could not read the user keymap {Path}", path);
            return [];
        }
    }

    private void Say(string text) => speechQueue?.Enqueue(new Utterance(text, SpeechPriority.Interrupt));

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
