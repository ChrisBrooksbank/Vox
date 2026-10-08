using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Diagnostics;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Text;

namespace Vox.App;

public static class ServiceRegistration
{
    public static void RegisterServices(HostBuilderContext context, IServiceCollection services)
    {
        // Settings
        services.AddSingleton<SettingsManager>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SettingsManager>>();
            var defaultSettingsPath = Path.Combine(
                AppContext.BaseDirectory,
                "assets", "config", "default-settings.json");
            var policy = sp.GetService<RunPolicy>() ?? RunPolicy.Normal;
            return new SettingsManager(logger, defaultSettingsPath, policy.SettingsPath)
            {
                ReadOnly = !policy.AllowSettingsWrites,
            };
        });
        services.AddSingleton<SettingsMonitor>();
        services.AddSingleton<IOptionsMonitor<VoxSettings>>(sp => sp.GetRequiredService<SettingsMonitor>());

        // Speech
        // The registry is the engine everything speaks through; it starts the configured engine
        // (OneCore by default) and falls back to SAPI
        services.AddSingleton<SpeechEngineRegistry>(sp =>
        {
            var player = sp.GetRequiredService<IAudioStreamPlayer>();
            var engines = new List<SpeechEngineDescriptor>
            {
                new(SpeechEngineRegistry.OneCoreId, "OneCore", () =>
                {
                    var synthesizer = new WinRtOneCoreSynthesizer();
                    if (synthesizer.Voices.Count == 0)
                    {
                        synthesizer.Dispose();
                        throw new InvalidOperationException("No OneCore voices are installed");
                    }
                    var engine = new WaveSpeechEngine(synthesizer, player, sp.GetRequiredService<ILogger<WaveSpeechEngine>>());
                    engine.WarmUp();
                    return engine;
                }),
            };
            // eSpeak NG is an optional component the user installs (GPL); never on secure screens
            var policy = sp.GetService<RunPolicy>() ?? RunPolicy.Normal;
            var espeakDirectory = EspeakComponent.DefaultDirectory;
            if (policy.AllowAddOns && EspeakComponent.IsInstalled(espeakDirectory))
            {
                engines.Add(new(SpeechEngineRegistry.EspeakId, "eSpeak NG", () =>
                    new WaveSpeechEngine(new EspeakSynthesizer(EspeakNative.Load(espeakDirectory)), player,
                        sp.GetRequiredService<ILogger<WaveSpeechEngine>>())));
            }
            engines.Add(new(SpeechEngineRegistry.SapiId, "SAPI 5",
                () => new SapiSpeechEngine(sp.GetRequiredService<ILogger<SapiSpeechEngine>>())));
            return new SpeechEngineRegistry(engines, sp.GetRequiredService<ILogger<SpeechEngineRegistry>>());
        });
        services.AddSingleton<ISpeechEngine>(sp => sp.GetRequiredService<SpeechEngineRegistry>());
        services.AddSingleton<SettingsRing>(sp => new SettingsRing(
            sp.GetRequiredService<ISpeechEngine>(),
            sp.GetRequiredService<SpeechEngineRegistry>().Engines,
            sp.GetRequiredService<IOptionsMonitor<VoxSettings>>(),
            sp.GetRequiredService<SettingsMonitor>().UpdateSettings,
            sp.GetRequiredService<SpeechQueue>()));
        // Rules run in this order on every utterance
        services.AddSingleton<PronunciationRule>(sp =>
        {
            var policy = sp.GetService<RunPolicy>() ?? RunPolicy.Normal;
            // Secure screens never read the user's profile
            var directory = policy.AllowUserProfileAccess
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vox", "dictionaries")
                : null;
            var engine = sp.GetRequiredService<ISpeechEngine>();
            var rule = new PronunciationRule(PronunciationRule.LoadBuiltInDefault(), directory, () => engine.CurrentVoice,
                sp.GetRequiredService<ILogger<PronunciationRule>>());
            rule.StartWatching();
            return rule;
        });
        services.AddSingleton<TextProcessor>(sp => new TextProcessor(
            [
                // First: the state marks must not reach any other rule
                new StateSoundsRule(),
                new LanguageRule(sp.GetRequiredService<ISpeechEngine>(),
                    () => sp.GetRequiredService<IOptionsMonitor<VoxSettings>>().CurrentValue),
                // On the text as it is on screen, before anything replaces words
                new CapitalsRule(() => sp.GetRequiredService<IOptionsMonitor<VoxSettings>>().CurrentValue),
                sp.GetRequiredService<PronunciationRule>(),
                new PunctuationRule(SymbolDictionary.LoadBuiltIn(),
                    () => sp.GetRequiredService<IOptionsMonitor<VoxSettings>>().CurrentValue.PunctuationLevel),
                // After symbols, which leave separators inside numbers alone
                new NumbersRule(() => sp.GetRequiredService<IOptionsMonitor<VoxSettings>>().CurrentValue.Numbers),
            ],
            sp.GetRequiredService<ILogger<TextProcessor>>()));
        services.AddSingleton<SpeechQueue>(sp =>
        {
            var queue = new SpeechQueue(sp.GetRequiredService<ISpeechEngine>(), sp.GetRequiredService<ILogger<SpeechQueue>>());
            queue.TextProcessor = sp.GetRequiredService<TextProcessor>();
            var cues = sp.GetRequiredService<IAudioCuePlayer>();
            queue.CuePlayer = cue =>
            {
                if (cue == Utterance.CapitalCue)
                    cues.PlayTone(2000, 30);
                else
                    cues.Play(cue);
            };
            var history = sp.GetRequiredService<SpeechHistory>();
            var latency = sp.GetRequiredService<LatencyTracker>();
            queue.UtteranceStarted += (_, u) =>
            {
                if (u.RecordInHistory)
                    history.Add(u.Text);
            };
            queue.UtteranceStarted += (_, _) => latency.NoteSpeechStarted();
            queue.QueueLatency = wait => latency.Record(LatencyTracker.QueueToEngine, wait);
            return queue;
        });
        services.AddSingleton<SpeechHistory>(_ => new SpeechHistory());
        services.AddSingleton<SpeechHistoryCommands>();
        services.AddSingleton<SleepMode>(sp => new SleepMode(
            sp.GetRequiredService<IForegroundWindow>(),
            sp.GetRequiredService<IOptionsMonitor<VoxSettings>>(),
            sp.GetRequiredService<SettingsMonitor>().UpdateSettings,
            sp.GetRequiredService<SpeechQueue>()));
        services.AddSingleton<LatencyTracker>(sp =>
        {
            var tracker = new LatencyTracker();
            tracker.StartReporting(sp.GetRequiredService<ILogger<LatencyTracker>>(), TimeSpan.FromMinutes(1));
            return tracker;
        });
        services.AddSingleton<SpeechViewer>();

        // Audio
        // One output for earcons and for speech engines that play their own audio (OneCore)
        services.AddSingleton<AudioCuePlayer>();
        services.AddSingleton<IAudioCuePlayer>(sp => sp.GetRequiredService<AudioCuePlayer>());
        services.AddSingleton<IAudioStreamPlayer>(sp => sp.GetRequiredService<AudioCuePlayer>());

        // Pipeline
        services.AddSingleton<EventPipeline>();
        services.AddSingleton<IEventSink>(sp => sp.GetRequiredService<EventPipeline>());

        // UIA Accessibility
        services.AddSingleton<UIAThread>();
        services.AddSingleton<UIAWatchdog>();
        services.AddSingleton<UIARecovery>();
        services.AddSingleton<IForegroundApp, Win32ForegroundApp>();
        services.AddSingleton<NotRespondingReporter>();
        services.AddSingleton<TextCaretTracker>(sp =>
        {
            var settings = sp.GetRequiredService<IOptionsMonitor<VoxSettings>>();
            var tracker = new TextCaretTracker(spellingErrors: () => settings.CurrentValue.SpellingErrors,
                indentation: () => settings.CurrentValue.Indentation);
            var cues = sp.GetRequiredService<IAudioCuePlayer>();
            tracker.SpellingErrorEntered += (_, _) => cues.Play("error");
            tracker.IndentationChanged += (_, columns) => cues.PlayTone(TextCaretTracker.IndentationToneHz(columns), 40);
            return tracker;
        });
        services.AddSingleton<IFocusedTextSource, UIAFocusedTextSource>();
        services.AddSingleton<FocusedTextMonitor>();
        services.AddSingleton<IFocusedTextReader>(sp => sp.GetRequiredService<FocusedTextMonitor>());
        services.AddSingleton<TerminalMonitor>();
        services.AddSingleton<IForegroundWindow, Win32ForegroundWindow>();
        services.AddSingleton<ForegroundWindowMonitor>();
        services.AddSingleton<DialogReader>();
        services.AddSingleton<MenuTracker>();
        services.AddSingleton<WhereAmICommands>();
        services.AddSingleton<INavigatorObjectSource>(sp => new UIANavigatorObjectSource(sp.GetRequiredService<UIAProvider>()));
        services.AddSingleton<IClipboard, StaClipboard>();
        services.AddSingleton<ObjectNavigationCommands>();
        services.AddSingleton<IReviewTextSource>(sp => new UIAReviewTextSource(sp.GetRequiredService<UIAProvider>(),
            sp.GetRequiredService<IFocusedTextSource>(), sp.GetRequiredService<IForegroundWindow>()));
        services.AddSingleton<ReviewCommands>();
        services.AddSingleton<IMousePointer, Win32MousePointer>();
        services.AddSingleton<IMouseInput, Win32MouseInput>();
        services.AddSingleton<MouseCommands>();
        services.AddSingleton<IPointerTargetSource>(sp => new UIAPointerTargetSource(sp.GetRequiredService<UIAProvider>()));
        services.AddSingleton<MouseTracker>(sp => ActivatorUtilities.CreateInstance<MouseTracker>(sp,
            new Action<VoxSettings>(sp.GetRequiredService<SettingsMonitor>().UpdateSettings)));
        services.AddSingleton<IStartupRegistration, RunKeyStartupRegistration>();
        services.AddSingleton<IAudioDucker, Win32AudioDucker>();
        services.AddSingleton<DuckingController>(sp =>
        {
            var controller = new DuckingController(sp.GetRequiredService<IAudioDucker>());
            var queue = sp.GetRequiredService<SpeechQueue>();
            queue.UtteranceStarted += (_, _) => controller.OnSpeechStarted();
            queue.UtteranceFinished += (_, _) => controller.OnSpeechEnded();
            return controller;
        });
        services.AddSingleton<ProgressReporter>(sp =>
        {
            var settings = sp.GetRequiredService<IOptionsMonitor<VoxSettings>>();
            var foreground = sp.GetRequiredService<IForegroundWindow>();
            return new ProgressReporter(() => settings.CurrentValue, () => foreground.Get()?.ProcessId);
        });
        services.AddSingleton<UIAProvider>();
        services.AddSingleton<UIAEventSubscriber>();
        services.AddSingleton<LiveRegionMonitor>();
        services.AddSingleton<BrowseDocumentTracker>();
        services.AddSingleton<IBrowseDocumentActions>(sp => sp.GetRequiredService<BrowseDocumentTracker>());

        // Input
        services.AddSingleton<IKeyboardHook, KeyboardHook>();
        services.AddSingleton<HookSafetyNet>();
        services.AddSingleton<KeyMap>(sp => LoadKeyMap(
            sp.GetRequiredService<IOptionsMonitor<VoxSettings>>().CurrentValue.KeyboardLayout,
            sp.GetRequiredService<ILogger<KeyMap>>()));
        services.AddSingleton<KeyInputDispatcher>(sp =>
        {
            var hook = sp.GetRequiredService<IKeyboardHook>();
            var keyMap = sp.GetRequiredService<KeyMap>();
            var pipeline = sp.GetRequiredService<EventPipeline>();
            var logger = sp.GetRequiredService<ILogger<KeyInputDispatcher>>();
            return new KeyInputDispatcher(hook, keyMap, pipeline, logger, sp.GetRequiredService<LatencyTracker>());
        });
        services.AddSingleton<TypingEchoHandler>(sp =>
        {
            var pipeline = sp.GetRequiredService<EventPipeline>();
            var settings = sp.GetRequiredService<IOptionsMonitor<VoxSettings>>();
            var logger = sp.GetRequiredService<ILogger<TypingEchoHandler>>();
            return new TypingEchoHandler(pipeline, () => settings.CurrentValue.TypingEchoMode, logger,
                KeyboardLayoutMapper.ToChar);
        });

        // First-run wizard
        services.AddSingleton<FirstRunWizard>();

        // Navigation
        services.AddSingleton<NavigationManager>();
        services.AddSingleton<QuickNavHandler>();
        services.AddSingleton<AnnouncementBuilder>(sp =>
        {
            var settings = sp.GetRequiredService<IOptionsMonitor<VoxSettings>>();
            return new AnnouncementBuilder { StatesAsSounds = () => settings.CurrentValue.StatesAsSounds };
        });
        services.AddSingleton<SayAllController>();
        services.AddSingleton<IElementsListPresenter, ElementsListPresenter>();
        // Header rows/columns set by hand; secure screens keep them for the session only
        services.AddSingleton<TableHeaderStore>(sp => new TableHeaderStore(
            (sp.GetService<RunPolicy>() ?? RunPolicy.Normal).AllowSettingsWrites ? TableHeaderStore.DefaultPath : null,
            sp.GetRequiredService<ILogger<TableHeaderStore>>()));
        services.AddSingleton<BrowseModeController>();

        // Hosted service
        services.AddHostedService<ScreenReaderService>();
    }

    /// <summary>
    /// Loads the keymap for <paramref name="layout"/> from assets/config, falling back to the
    /// built-in one; problems are logged.
    /// </summary>
    public static KeyMap LoadKeyMap(KeyboardLayout layout, ILogger logger)
    {
        var configDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "config");
        var keyMap = KeyMap.LoadLayout(configDirectory, layout, out var error, out var warnings);
        if (error is not null)
        {
            logger.LogError(error,
                "Could not load the {Layout} keymap from {Path}; using the built-in one", layout, configDirectory);
        }
        foreach (var warning in warnings)
            logger.LogWarning("Keymap: {Warning}", warning);
        return keyMap;
    }
}
