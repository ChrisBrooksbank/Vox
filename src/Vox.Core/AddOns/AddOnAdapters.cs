using Vox.Core.AppModules;
using Vox.Core.Braille;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Sdk;
using CoreAppModule = Vox.Core.AppModules.IAppModule;
using CoreSpeechEngine = Vox.Core.Speech.ISpeechEngine;
using CoreSpeechPriority = Vox.Core.Speech.SpeechPriority;
using SdkAppModule = Vox.Sdk.IAppModule;
using SdkSpeechEngine = Vox.Sdk.ISpeechEngine;
using SdkSpeechPriority = Vox.Sdk.SpeechPriority;

namespace Vox.Core.AddOns;

/// <summary>Vox's events and priorities as the add-on API has them.</summary>
public static class SdkEvents
{
    /// <summary>
    /// <paramref name="evt"/> for add-ons, or null for events they never see: keys and typed text
    /// (never handed to add-ons), Vox's own commands (an add-on can't drop those) and internal work.
    /// </summary>
    public static VoxEvent? From(ScreenReaderEvent evt) => evt switch
    {
        FocusChangedEvent focus => new FocusEvent(focus.Timestamp, Focus(focus)),
        ForegroundWindowChangedEvent window => new ForegroundWindowEvent(window.Timestamp, window.Title, window.ProcessId),
        Pipeline.NotificationEvent { NotificationText: { } text } notification => new Sdk.NotificationEvent(notification.Timestamp, text, notification.ActivityId),
        LiveRegionChangedEvent live => new LiveRegionEvent(live.Timestamp, live.Text, live.Politeness == LiveRegionPoliteness.Assertive),
        Pipeline.CaretMovedEvent caret => new Sdk.CaretMovedEvent(caret.Timestamp),
        TextEditedEvent edited => new TextChangedEvent(edited.Timestamp),
        RawKeyEvent or TypingEchoEvent or InputHelpEvent or NavigationCommandEvent or AddOnCommandEvent or PipelineWorkEvent => null,
        _ => new OtherEvent(evt.Timestamp, evt.GetType().Name.Replace("Event", string.Empty)),
    };

    /// <summary>The focused object; a password field's value is never given.</summary>
    public static FocusInfo Focus(FocusChangedEvent focus) => new(
        focus.ElementName, focus.ControlType, focus.AriaRole, focus.IsPassword ? null : focus.Value, focus.Description, focus.IsPassword);

    public static CoreSpeechPriority Priority(SdkSpeechPriority priority) => priority switch
    {
        SdkSpeechPriority.Interrupt => CoreSpeechPriority.Interrupt,
        SdkSpeechPriority.High => CoreSpeechPriority.High,
        SdkSpeechPriority.Low => CoreSpeechPriority.Low,
        _ => CoreSpeechPriority.Normal,
    };

    public static SdkSpeechPriority Priority(CoreSpeechPriority priority) => priority switch
    {
        CoreSpeechPriority.Interrupt => SdkSpeechPriority.Interrupt,
        CoreSpeechPriority.High => SdkSpeechPriority.High,
        CoreSpeechPriority.Low => SdkSpeechPriority.Low,
        _ => SdkSpeechPriority.Normal,
    };
}

/// <summary>An add-on's app module as one of Vox's: every call guarded; its keys are the add-on key layer's.</summary>
public sealed class AddOnAppModule : CoreAppModule
{
    private readonly SdkAppModule _module;
    private readonly AddOnGuard _guard;

    public AddOnAppModule(SdkAppModule module, AddOnGuard guard)
    {
        _module = module;
        _guard = guard;
        // Read once: Vox asks for these on every focus change
        Id = module.Id;
        Name = guard.Call(() => module.Name, module.Id, "naming its app module");
        ProcessNames = guard.Call(() => module.ProcessNames.ToList(), [], "listing its app module's processes");
    }

    public string AddOnId => _guard.AddOnId;
    public string Id { get; }
    public string Name { get; }
    public IReadOnlyCollection<string> ProcessNames { get; }
    public IReadOnlyList<KeyBinding> KeyBindings => [];

    /// <summary>The module's commands (empty when it fails to list them).</summary>
    public IReadOnlyList<AddOnCommand> Commands => _guard.Call(() => _module.Commands ?? [], [], "listing its app module's commands");

    public void OnActivated(AppActivation activation) =>
        _guard.Run(() => _module.OnActivated(new AppInfo(activation.ProcessId, activation.ProcessName, activation.WindowHandle)), "activating its app module");

    public void OnDeactivated() => _guard.Run(_module.OnDeactivated, "deactivating its app module");

    public bool TryHandle(NavigationCommand command) => false;

    public string? FormatFocusAnnouncement(FocusChangedEvent focus, string announcement) =>
        _guard.Call(() => _module.FormatFocusAnnouncement(SdkEvents.Focus(focus), announcement), announcement, "formatting a focus announcement");

    public bool ShouldProcess(ScreenReaderEvent evt) =>
        SdkEvents.From(evt) is not { } sdk || _guard.Call(() => _module.ShouldProcess(sdk), true, "filtering an event");

    public void OnEvent(ScreenReaderEvent evt)
    {
        if (SdkEvents.From(evt) is { } sdk)
            _guard.Run(() => _module.OnEvent(sdk), "handling an event");
    }
}

/// <summary>An add-on's global plug-in with its guard.</summary>
public sealed record GuardedPlugin(IGlobalPlugin Plugin, AddOnGuard Guard);

/// <summary>An add-on's announcement filter with its guard.</summary>
public sealed record GuardedFilter(IAnnouncementFilter Filter, AddOnGuard Guard);

/// <summary>
/// The add-ons' announcement filters as one step of Vox's text processing (after dictionaries,
/// before symbols). A filter that throws is skipped for that utterance and counted against its add-on.
/// </summary>
public sealed class AddOnTextRule(Func<IReadOnlyList<GuardedFilter>> filters) : ITextRule
{
    public string Apply(string text, Utterance utterance)
    {
        var current = filters();
        if (current.Count == 0)
            return text;
        var info = new AnnouncementInfo(SdkEvents.Priority(utterance.Priority), utterance.Language);
        foreach (var (filter, guard) in current)
        {
            var input = text;
            text = guard.Call(() => filter.Filter(input, info) ?? input, input, "filtering speech");
        }
        return text;
    }
}

/// <summary>An add-on's speech engine as one of Vox's. A failure while speaking lets the registry fall back to the next engine.</summary>
public sealed class AddOnSpeechEngine(SdkSpeechEngine engine, AddOnGuard guard) : CoreSpeechEngine, IDisposable
{
    private int _pitch = CoreSpeechEngine.DefaultPitch;

    public bool IsSpeaking => guard.Call(() => engine.IsSpeaking, false, "reporting whether it speaks");

    public async Task SpeakAsync(Utterance utterance, CancellationToken cancellationToken = default)
    {
        if (guard.IsStopped)
            throw new InvalidOperationException($"Add-on {guard.AddOnId} is turned off");
        try
        {
            await engine.SpeakAsync(utterance.Text, new SpeakOptions(utterance.Voice, utterance.PitchOffset), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            guard.Failed(ex, "speaking");
            throw;
        }
    }

    public void Cancel() => guard.Run(engine.Cancel, "stopping speech");
    public void SetRate(int wpm) => guard.Run(() => engine.SetRate(wpm), "setting the rate");
    public void SetVoice(string voiceName) => guard.Run(() => engine.SetVoice(voiceName), "setting the voice");

    public IReadOnlyList<string> GetAvailableVoices() => GetVoiceDetails().Select(v => v.Name).ToList();

    public IReadOnlyList<SpeechVoice> GetVoiceDetails() =>
        guard.Call(() => engine.GetVoices().Select(v => new SpeechVoice(v.Name, v.Language ?? string.Empty)).ToList(), [], "listing voices");

    public IReadOnlyList<string> GetLanguages() =>
        GetVoiceDetails().Select(v => v.Language).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public int MaxRateWpm => guard.Call(() => engine.MaxRateWpm, 450, "reporting its fastest rate");
    public SpeechCapabilities Capabilities => SpeechCapabilities.Pitch | SpeechCapabilities.Volume;

    public void SetPitch(int pitch)
    {
        _pitch = pitch;
        guard.Run(() => engine.SetPitch(pitch), "setting the pitch");
    }

    public void SetVolume(int volume) => guard.Run(() => engine.SetVolume(volume), "setting the volume");

    public void Dispose() => guard.Cleanup(engine.Dispose, "closing its speech engine");
}

/// <summary>An add-on's braille driver as one of Vox's, every call guarded.</summary>
public sealed class AddOnBrailleDriver : IBrailleDisplayDriver
{
    private readonly IBrailleDriver _driver;
    private readonly AddOnGuard _guard;

    public AddOnBrailleDriver(IBrailleDriver driver, AddOnGuard guard)
    {
        _driver = driver;
        _guard = guard;
        Id = guard.Call(() => driver.Id, guard.AddOnId, "naming its braille driver");
        Name = guard.Call(() => driver.Name, Id, "naming its braille display");
        guard.Run(() =>
        {
            driver.KeysPressed += OnKeysPressed;
            driver.Disconnected += OnDisconnected;
        }, "subscribing to its braille display");
    }

    public string AddOnId => _guard.AddOnId;
    public string Id { get; }
    public string Name { get; }
    public bool IsConnected => _guard.Call(() => _driver.IsConnected, false, "reporting its display");
    public int CellCount => _guard.Call(() => _driver.CellCount, 0, "reporting its cells");

    public bool TryConnect() => _guard.Call(_driver.TryConnect, false, "connecting a braille display");
    public void Disconnect() => _guard.Cleanup(_driver.Disconnect, "disconnecting a braille display");
    public void Write(ReadOnlyMemory<byte> cells, int? cursorPosition) => _guard.Run(() => _driver.Write(cells, cursorPosition), "writing braille");

    public event EventHandler<BrailleKeyEventArgs>? KeyPressed;
    public event EventHandler? Disconnected;

    private void OnKeysPressed(object? sender, BrailleKeysEventArgs e)
    {
        if (!_guard.IsStopped && e?.Keys is { } keys)
            KeyPressed?.Invoke(this, new BrailleKeyEventArgs(keys.ToList(), e.RoutingCell));
    }

    private void OnDisconnected(object? sender, EventArgs e) => Disconnected?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _guard.Cleanup(() =>
        {
            _driver.KeysPressed -= OnKeysPressed;
            _driver.Disconnected -= OnDisconnected;
            _driver.Dispose();
        }, "closing its braille driver");
    }
}
