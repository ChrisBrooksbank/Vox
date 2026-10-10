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
    ICommandSearchPresenter Search,
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
    GestureSetup? gestures = null,
    IVoxMenuPresenter? menu = null,
    IPortableCopyPresenter? portable = null)
{
    public bool TryHandle(NavigationCommand command)
    {
        switch (command)
        {
            case NavigationCommand.CreatePortableCopy:
                // Needs the user's own folder, which the secure screens never touch
                if (portable is null || gestures?.UserKeyMapPath is null)
                    Say("A portable copy can't be made here");
                else if (browse.BeginOwnDialog())
                    _ = CreatePortableCopyAsync(portable);
                return true;
            case NavigationCommand.OpenVoxMenu:
                if (menu is not null && browse.BeginOwnDialog())
                    _ = ShowMenuAsync(menu);
                return true;
            case NavigationCommand.OpenSettings:
                if (browse.BeginOwnDialog())
                    _ = ShowSettingsAsync();
                return true;
            case NavigationCommand.OpenCommandSearch:
                if (gestures is null)
                    Say("Command search isn't available here");
                else if (browse.BeginOwnDialog())
                    _ = ShowCommandSearchAsync(gestures);
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

    private async Task ShowMenuAsync(IVoxMenuPresenter presenter)
    {
        NavigationCommand? chosen = null;
        try
        {
            chosen = await presenter.ShowAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Vox menu failed");
        }
        // The menu's dialog is closed first, so the next dialog can open
        pipeline.Post(new VoxDialogClosedEvent(DateTimeOffset.UtcNow));
        if (chosen is { } command)
            pipeline.Post(new NavigationCommandEvent(DateTimeOffset.UtcNow, command));
    }

    private async Task CreatePortableCopyAsync(IPortableCopyPresenter presenter)
    {
        PortableCopyRequest? request = null;
        try
        {
            request = await presenter.ShowAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Portable copy dialog failed");
        }
        pipeline.Post(new VoxDialogClosedEvent(DateTimeOffset.UtcNow));
        if (request is null)
            return;

        Say("Creating the portable copy");
        try
        {
            int files = await Task.Run(() => Lifecycle.PortableCopy.Create(
                AppContext.BaseDirectory, request.Folder,
                request.CopySettings ? Lifecycle.VoxPaths.UserData : null,
                request.CopyComponents ? Lifecycle.VoxPaths.Components : null)).ConfigureAwait(false);
            Say($"Portable copy created in {request.Folder}");
            logger.LogInformation("Portable copy of {Files} files created", files);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not create the portable copy");
            Say($"The portable copy could not be created: {ex.Message}");
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

    private async Task ShowCommandSearchAsync(GestureSetup setup)
    {
        try
        {
            var layout = KeyMap.LayoutBindings(setup.ConfigDirectory, settings.CurrentValue.KeyboardLayout);
            var keys = new GestureEditor(layout, setup.UserKeyMapPath is { } path ? ReadUserKeyMap(path) : []);
            var screenReaderKey = settings.CurrentValue.ModifierKey == ModifierKey.CapsLock ? "Caps Lock" : "Insert";
            await setup.Search.ShowAsync(query => CommandSearch.Find(query, keys, screenReaderKey)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Command search failed");
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
