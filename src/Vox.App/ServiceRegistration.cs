using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.Ai;
using Vox.Core.AppModules;
using Vox.Core.AppModules.Excel;
using Vox.Core.AppModules.Explorer;
using Vox.Core.AppModules.Office;
using Vox.Core.AppModules.Outlook;
using Vox.Core.AppModules.PowerPoint;
using Vox.Core.AppModules.Teams;
using Vox.Core.AppModules.Word;
using Vox.Core.Audio;
using Vox.Core.Braille;
using Vox.Core.Braille.Brltty;
using Vox.Core.Braille.Hid;
using Vox.Core.Configuration;
using Vox.Core.Diagnostics;
using Vox.Core.Enterprise;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Localization;
using Vox.Core.MathSpeech;
using Vox.Core.Navigation;
using Vox.Core.Ocr;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Text;
using Vox.Core.Updates;

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
                // What the administrator set for new users (the installer's properties, machine-settings.json)
                MachineDefaults = MachineSettings.Read(sp.GetRequiredService<IRegistryReader>(), MachineSettings.FilePath, logger),
            };
        });
        services.AddSingleton<IRegistryReader, Win32RegistryReader>();
        // The organization's policies (Program.cs reads them first, as they shape RunPolicy and paths)
        services.TryAddSingleton(sp => PolicySettings.Read(sp.GetRequiredService<IRegistryReader>(),
            includeUser: !(sp.GetService<RunPolicy>() ?? RunPolicy.Normal).IsSecure));
        services.AddSingleton<SettingsMonitor>();
        // Configuration profiles, laid over the settings as soon as they exist; none on secure screens
        services.AddSingleton<ProfileManager>(sp =>
        {
            var policy = sp.GetService<RunPolicy>() ?? RunPolicy.Normal;
            var profiles = new ProfileManager(new ProfileStore(
                policy.AllowSettingsWrites && policy.AllowUserProfileAccess ? ProfileStore.DefaultDirectory : null,
                sp.GetRequiredService<ILogger<ProfileStore>>()));
            sp.GetRequiredService<SettingsMonitor>().AttachProfiles(profiles);
            return profiles;
        });
        services.AddSingleton<IProfilesPresenter, ProfilesPresenter>();
        services.AddSingleton<ProfileCommands>(sp =>
        {
            var appModules = sp.GetRequiredService<AppModuleManager>();
            return new ProfileCommands(
                sp.GetRequiredService<IProfilesPresenter>(),
                sp.GetRequiredService<ProfileManager>(),
                () => appModules.ActiveProcessName,
                sp.GetRequiredService<BrowseModeController>(),
                sp.GetRequiredService<IEventSink>(),
                sp.GetRequiredService<SpeechQueue>(),
                sp.GetRequiredService<ILogger<ProfileCommands>>());
        });
        services.AddSingleton<IOptionsMonitor<VoxSettings>>(sp => sp.GetRequiredService<SettingsMonitor>());

        // Strings: every IStringLocalizer<T> reads Localization/Strings.resx in the Language setting
        services.AddSingleton<IStringLocalizerFactory, VoxStringLocalizerFactory>();
        services.AddTransient(typeof(IStringLocalizer<>), typeof(StringLocalizer<>));

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
                ? Path.Combine(VoxPaths.UserData, "dictionaries")
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
                // Symbols named in the page's language, else Vox's
                new PunctuationRule(SymbolDictionary.ForLanguage,
                    () => sp.GetRequiredService<IOptionsMonitor<VoxSettings>>().CurrentValue.PunctuationLevel,
                    () => VoxStrings.EffectiveCulture.TwoLetterISOLanguageName),
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
        // Remote Desktop / Citrix: said at startup; some audio settings are off remotely
        services.AddSingleton<IRemoteSessionProbe, Win32RemoteSessionProbe>();
        services.AddSingleton<RemoteSessionMonitor>();
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
        // Text recognition: on this computer, never on secure screens
        services.AddSingleton<IOcrEngine, WindowsOcrEngine>();
        services.AddSingleton<IScreenCapture>(sp => new GdiScreenCapture(sp.GetService<RunPolicy>() ?? RunPolicy.Normal));
        services.AddSingleton<IOcrTargetSource, UIAOcrTargetSource>();
        services.AddSingleton<OcrCommands>();

        // Input
        services.AddSingleton<IKeyboardHook, KeyboardHook>();
        services.AddSingleton<HookSafetyNet>();
        services.AddSingleton<KeyMap>(sp => LoadKeyMap(
            sp.GetRequiredService<IOptionsMonitor<VoxSettings>>().CurrentValue.KeyboardLayout,
            sp.GetRequiredService<ILogger<KeyMap>>(),
            sp.GetService<RunPolicy>() ?? RunPolicy.Normal));
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
        services.AddSingleton<FirstRunWizard>(sp =>
        {
            var policy = sp.GetService<RunPolicy>() ?? RunPolicy.Normal;
            var settings = sp.GetRequiredService<SettingsMonitor>();
            return new FirstRunWizard(
                sp.GetRequiredService<ISpeechEngine>(),
                sp.GetRequiredService<SettingsManager>(),
                settings,
                sp.GetRequiredService<IKeyboardHook>(),
                sp.GetRequiredService<ILogger<FirstRunWizard>>(),
                // The practice lessons teach the keys in use: the layout's and the user's own
                () => new GestureEditor(
                    KeyMap.LayoutBindings(Path.Combine(AppContext.BaseDirectory, "assets", "config"), settings.CurrentValue.KeyboardLayout),
                    KeyMap.ReadUserBindings(policy.AllowUserProfileAccess ? KeyMap.DefaultUserKeyMapPath : null)));
        });

        // Navigation
        services.AddSingleton<NavigationManager>();
        services.AddSingleton<QuickNavHandler>();
        services.AddSingleton<AnnouncementBuilder>(sp =>
        {
            var settings = sp.GetRequiredService<IOptionsMonitor<VoxSettings>>();
            return new AnnouncementBuilder(sp.GetRequiredService<IStringLocalizer<AnnouncementBuilder>>())
            {
                StatesAsSounds = () => settings.CurrentValue.StatesAsSounds,
            };
        });
        services.AddSingleton<SayAllController>();
        services.AddSingleton<IElementsListPresenter, ElementsListPresenter>();
        services.AddSingleton<IFindPrompt, FindPromptPresenter>();
        // Header rows/columns set by hand; secure screens keep them for the session only
        services.AddSingleton<TableHeaderStore>(sp => new TableHeaderStore(
            (sp.GetService<RunPolicy>() ?? RunPolicy.Normal).AllowSettingsWrites ? TableHeaderStore.DefaultPath : null,
            sp.GetRequiredService<ILogger<TableHeaderStore>>()));
        // MathCAT, when installed as an optional component (never on secure screens)
        services.AddSingleton<IMathSpeech>(sp =>
        {
            var policy = sp.GetService<RunPolicy>() ?? RunPolicy.Normal;
            var directory = MathCatComponent.DefaultDirectory;
            if (policy.AllowAddOns && MathCatComponent.IsInstalled(directory))
            {
                var logger = sp.GetRequiredService<ILogger<MathCatSpeech>>();
                try { return MathCatSpeech.Load(directory, logger); }
                catch (Exception ex) { logger.LogWarning(ex, "Could not load MathCAT"); }
            }
            return new NoMathSpeech();
        });
        services.AddSingleton<BrowseModeController>();
        services.AddSingleton<ISettingsDialogPresenter, SettingsDialogPresenter>();
        services.AddSingleton<IInputGesturesPresenter, InputGesturesPresenter>();
        services.AddSingleton<ICommandSearchPresenter, CommandSearchPresenter>();
        services.AddSingleton<IVoxMenuPresenter, VoxMenuPresenter>();
        services.AddSingleton<IPortableCopyPresenter, PortableCopyPresenter>();
        services.AddSingleton<IConfirmPresenter, ConfirmPresenter>();
        services.AddSingleton<ITextPrompt, TextPromptPresenter>();
        services.AddSingleton<OwnDialogRunner>(sp => new OwnDialogRunner(
            sp.GetRequiredService<BrowseModeController>(), sp.GetRequiredService<EventPipeline>()));
        // AI: a model on this computer first, else the cloud provider the user set up, never
        // without consent, never on secure screens (docs/ai.md)
        services.AddSingleton<IAiKeyStore>(sp =>
        {
            var policy = sp.GetService<RunPolicy>() ?? RunPolicy.Normal;
            return new ProtectedAiKeyStore(policy.AllowUserProfileAccess && policy.AllowAi ? ProtectedAiKeyStore.DefaultPath : null,
                sp.GetRequiredService<ILogger<ProtectedAiKeyStore>>());
        });
        services.AddSingleton<LocalImageDescriber>(sp => new LocalImageDescriber(
            // Only ever an address on this computer, which the gate allows with the network off
            NetworkGate.CreateHttpClient(sp.GetService<RunPolicy>() ?? RunPolicy.Normal, TimeSpan.FromMinutes(3)),
            sp.GetRequiredService<IOptionsMonitor<VoxSettings>>(),
            sp.GetRequiredService<ILogger<LocalImageDescriber>>()));
        services.AddSingleton<CloudImageDescriber>(sp => new CloudImageDescriber(
            NetworkGate.CreateHttpClient(sp.GetService<RunPolicy>() ?? RunPolicy.Normal, TimeSpan.FromSeconds(90)),
            sp.GetRequiredService<IOptionsMonitor<VoxSettings>>(),
            sp.GetRequiredService<IAiKeyStore>(),
            sp.GetRequiredService<ILogger<CloudImageDescriber>>()));
        services.AddSingleton<AiConsent>(sp => new AiConsent(
            sp.GetService<RunPolicy>() ?? RunPolicy.Normal, sp.GetRequiredService<IOptionsMonitor<VoxSettings>>()));
        services.AddSingleton<IAiConsentPrompt, AiConsentPrompt>();
        services.AddSingleton<DescriptionCache>(_ => new DescriptionCache());
        services.AddSingleton<AiAssistant>(sp => new AiAssistant(
            sp.GetRequiredService<LocalImageDescriber>(),
            sp.GetRequiredService<CloudImageDescriber>(),
            sp.GetRequiredService<AiConsent>(),
            sp.GetRequiredService<IAiConsentPrompt>(),
            sp.GetRequiredService<DescriptionCache>(),
            sp.GetRequiredService<ILogger<AiAssistant>>()));
        services.AddSingleton<IAiContextSource, UIAAiContextSource>();
        services.AddSingleton<AiCommands>(sp => new AiCommands(
            sp.GetRequiredService<AiAssistant>(),
            sp.GetRequiredService<IScreenCapture>(),
            sp.GetRequiredService<IOcrTargetSource>(),
            sp.GetRequiredService<IAiContextSource>(),
            sp.GetRequiredService<ReviewCommands>(),
            sp.GetRequiredService<ITextPrompt>(),
            sp.GetRequiredService<OwnDialogRunner>(),
            sp.GetRequiredService<IAiKeyStore>(),
            sp.GetService<RunPolicy>() ?? RunPolicy.Normal,
            sp.GetRequiredService<IAudioCuePlayer>(),
            sp.GetRequiredService<SpeechQueue>(),
            sp.GetRequiredService<IOptionsMonitor<VoxSettings>>(),
            sp.GetRequiredService<ILogger<AiCommands>>(),
            sp.GetRequiredService<SettingsMonitor>().UpdateSettings));
        // Updates: only with the release key shipped beside Vox, and never on secure screens
        services.AddSingleton<UpdateChecker>(sp =>
        {
            var policy = sp.GetService<RunPolicy>() ?? RunPolicy.Normal;
            var settings = sp.GetRequiredService<IOptionsMonitor<VoxSettings>>();
            var key = policy.AllowNetwork ? UpdateChecker.ReadPublicKey(Path.Combine(AppContext.BaseDirectory, "assets", "config")) : null;
            return new UpdateChecker(
                // Refuses every request while the network is off (DisableNetwork policy)
                NetworkGate.CreateHttpClient(policy, TimeSpan.FromSeconds(60)),
                () => settings.CurrentValue.UpdateChannel,
                key,
                UpdateChecker.DefaultFeedUrl,
                typeof(ServiceRegistration).Assembly.GetName().Version ?? new Version(0, 1),
                sp.GetRequiredService<ILogger<UpdateChecker>>(),
                policy);
        });
        services.AddSingleton<UpdateCommands>(sp =>
        {
            var policy = sp.GetService<RunPolicy>() ?? RunPolicy.Normal;
            var lifetime = sp.GetRequiredService<IHostApplicationLifetime>();
            return new UpdateCommands(
                sp.GetRequiredService<UpdateChecker>(),
                sp.GetRequiredService<IConfirmPresenter>(),
                sp.GetRequiredService<BrowseModeController>(),
                sp.GetRequiredService<EventPipeline>(),
                sp.GetRequiredService<SpeechQueue>(),
                lifetime.StopApplication,
                policy.AllowSettingsWrites ? Path.Combine(VoxPaths.UserData, "update-check.txt") : null,
                sp.GetRequiredService<ILogger<UpdateCommands>>());
        });
        services.AddSingleton<SettingsCommands>(sp =>
        {
            var policy = sp.GetService<RunPolicy>() ?? RunPolicy.Normal;
            var settings = sp.GetRequiredService<IOptionsMonitor<VoxSettings>>();
            var dispatcher = sp.GetRequiredService<KeyInputDispatcher>();
            var keyMapLogger = sp.GetRequiredService<ILogger<KeyMap>>();
            var gestures = new GestureSetup(
                sp.GetRequiredService<IInputGesturesPresenter>(),
                sp.GetRequiredService<ICommandSearchPresenter>(),
                Path.Combine(AppContext.BaseDirectory, "assets", "config"),
                // Secure screens never change the user's keys
                policy.AllowSettingsWrites && policy.AllowUserProfileAccess ? KeyMap.DefaultUserKeyMapPath : null,
                dispatcher,
                () => dispatcher.SetKeyMap(LoadKeyMap(settings.CurrentValue.KeyboardLayout, keyMapLogger, policy)));
            return new SettingsCommands(
                sp.GetRequiredService<ISettingsDialogPresenter>(),
                settings,
                sp.GetRequiredService<SettingsMonitor>().UpdateSettings,
                sp.GetRequiredService<SpeechEngineRegistry>(),
                sp.GetRequiredService<BrowseModeController>(),
                sp.GetRequiredService<EventPipeline>(),
                sp.GetRequiredService<ILogger<SettingsCommands>>(),
                sp.GetRequiredService<SpeechQueue>(),
                gestures,
                sp.GetRequiredService<IVoxMenuPresenter>(),
                sp.GetRequiredService<IPortableCopyPresenter>(),
                sp.GetRequiredService<PolicySettings>());
        });

        // App modules: Vox's support for particular applications, chosen by the foreground
        // process. Office's object model is read on its own STA thread.
        services.AddSingleton<OfficeObjectModel>();
        services.AddSingleton<IOfficeObjectModel>(sp => sp.GetRequiredService<OfficeObjectModel>());
        services.AddSingleton<ExcelTitleStore>(sp => new ExcelTitleStore(
            (sp.GetService<RunPolicy>() ?? RunPolicy.Normal).AllowSettingsWrites ? ExcelTitleStore.DefaultPath : null,
            sp.GetRequiredService<ILogger<TableHeaderStore>>()));
        services.AddSingleton<IExcelSource, ExcelObjectModelSource>();
        services.AddSingleton<IAppModule, ExcelModule>();
        services.AddSingleton<IWordSource, WordObjectModelSource>();
        services.AddSingleton<IAppModule, WordModule>();
        services.AddSingleton<IOutlookSource, OutlookObjectModelSource>();
        services.AddSingleton<IAppModule, OutlookModule>();
        services.AddSingleton<IPowerPointSource, PowerPointObjectModelSource>();
        services.AddSingleton<IAppModule>(sp => new PowerPointModule(
            sp.GetRequiredService<IPowerPointSource>(), sp.GetRequiredService<SpeechQueue>(),
            sp.GetRequiredService<IEventSink>(), sp.GetRequiredService<ILogger<PowerPointModule>>()));
        services.AddSingleton<UIAAppSources>();
        services.AddSingleton<IWindowButtonSource>(sp => sp.GetRequiredService<UIAAppSources>());
        services.AddSingleton<IDetailsRowSource>(sp => sp.GetRequiredService<UIAAppSources>());
        // What Vox says, so applications' own announcements of the same aren't said again
        services.AddSingleton<RecentSpeech>(sp =>
        {
            var recent = new RecentSpeech();
            sp.GetRequiredService<SpeechQueue>().UtteranceQueued += (_, u) => recent.Add(u.Text);
            return recent;
        });
        services.AddSingleton<IAppModule, ExplorerModule>();
        services.AddSingleton<IAppModule>(sp => new TeamsModule(
            sp.GetRequiredService<IWindowButtonSource>(), sp.GetRequiredService<SpeechQueue>(), sp.GetRequiredService<IEventSink>(),
            sp.GetRequiredService<RecentSpeech>(), sp.GetRequiredService<IOptionsMonitor<VoxSettings>>(),
            () => sp.GetRequiredService<QuickNavHandler>().CurrentDocument));
        services.AddSingleton<IAppModule>(sp => new ZoomModule(
            sp.GetRequiredService<IWindowButtonSource>(), sp.GetRequiredService<SpeechQueue>(), sp.GetRequiredService<IEventSink>(),
            sp.GetRequiredService<RecentSpeech>()));
        services.AddSingleton<IAppModule>(sp => QuietAppModule.VisualStudioCode(sp.GetRequiredService<RecentSpeech>()));
        services.AddSingleton<IAppModule>(sp => QuietAppModule.VisualStudio(sp.GetRequiredService<RecentSpeech>()));
        services.AddSingleton<IAppModule>(sp => QuietAppModule.Acrobat(sp.GetRequiredService<RecentSpeech>()));
        services.AddSingleton<AppModuleManager>(sp =>
        {
            var dispatcher = sp.GetRequiredService<KeyInputDispatcher>();
            return new AppModuleManager(
                sp.GetServices<IAppModule>(),
                sp.GetRequiredService<IForegroundWindow>(),
                dispatcher.SetAppKeyMap,
                sp.GetRequiredService<IOptionsMonitor<VoxSettings>>(),
                sp.GetRequiredService<ILogger<AppModuleManager>>());
        });

        // Braille: display drivers in the order auto-detect tries them, then the controller
        services.AddSingleton<IKeyboardOutput, SendInputKeyboardOutput>();
        services.AddSingleton<IBrailleDisplayDriver>(sp => new HidBrailleDisplay(sp.GetRequiredService<ILogger<HidBrailleDisplay>>()));
        services.AddSingleton<IBrailleDisplayDriver>(sp => new BrlttyDisplay(logger: sp.GetRequiredService<ILogger<BrlttyDisplay>>(),
            policy: sp.GetService<RunPolicy>() ?? RunPolicy.Normal));
        services.AddSingleton<BrailleDisplayManager>();
        services.AddSingleton<IBrailleDisplaySource>(sp => sp.GetRequiredService<BrailleDisplayManager>());
        services.AddSingleton<UIAFocusAncestry>();
        services.AddSingleton<IBrailleRouter, BrailleRouter>();
        services.AddSingleton<BrailleController>(sp => new BrailleController(
            sp.GetRequiredService<IBrailleDisplaySource>(),
            sp.GetRequiredService<IOptionsMonitor<VoxSettings>>(),
            sp.GetRequiredService<IBrailleRouter>(),
            sp.GetRequiredService<IKeyboardOutput>(),
            sp.GetRequiredService<ILogger<BrailleController>>(),
            // liblouis is an add-on: never loaded in secure mode
            translatorFactory: (sp.GetService<RunPolicy>() ?? RunPolicy.Normal).AllowAddOns
                ? table => BrailleTranslatorFactory.Create(table, null, sp.GetRequiredService<ILogger<BrailleController>>())
                : _ => new ComputerBrailleTranslator()));

        // Hosted services: ScreenReaderService first, so its pipeline handlers run before braille's
        services.AddHostedService<ScreenReaderService>();
        services.AddHostedService<BrailleService>();
    }

    /// <summary>
    /// Loads the keymap for <paramref name="layout"/> from assets/config, falling back to the
    /// built-in one, with the user's keymap layered on top (not on secure screens); problems and
    /// conflicts are logged.
    /// </summary>
    public static KeyMap LoadKeyMap(KeyboardLayout layout, ILogger logger, RunPolicy policy)
    {
        var configDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "config");
        // The user's own bindings (%APPDATA%\Vox\keymap.json); never on secure screens
        var userKeyMap = policy.AllowUserProfileAccess ? KeyMap.DefaultUserKeyMapPath : null;
        var keyMap = KeyMap.LoadLayout(configDirectory, layout, userKeyMap, out var error, out var warnings);
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
