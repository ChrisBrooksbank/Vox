using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.AddOns;
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
using Vox.Core.Labels;
using Vox.Core.Lifecycle;
using Vox.Core.Localization;
using Vox.Core.MathSpeech;
using Vox.Core.Navigation;
using Vox.Core.Ocr;
using Vox.Core.Pipeline;
using Vox.Core.Remote;
using Vox.Core.Speech;
using Vox.Core.Switching;
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
            bool keep = policy.AllowSettingsWrites && policy.AllowUserProfileAccess;
            var profiles = new ProfileManager(
                new ProfileStore(keep ? ProfileStore.DefaultDirectory : null, sp.GetRequiredService<ILogger<ProfileStore>>()),
                new SiteSettingsStore(keep ? Path.Combine(VoxPaths.UserData, SiteSettingsStore.FileName) : null, sp.GetRequiredService<ILogger<SiteSettingsStore>>()));
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
        // Settings for the web site being browsed, laid over the application's profile
        services.AddSingleton<ISiteSettingsPresenter, SiteSettingsPresenter>();
        services.AddSingleton<SiteSettingsCommands>(sp => new SiteSettingsCommands(
            sp.GetRequiredService<ProfileManager>(),
            sp.GetRequiredService<ISiteSettingsPresenter>(),
            sp.GetRequiredService<SettingsMonitor>(),
            sp.GetRequiredService<BrowseModeController>(),
            sp.GetRequiredService<IEventSink>(),
            sp.GetRequiredService<ILogger<SiteSettingsCommands>>(),
            sp.GetRequiredService<SpeechQueue>()));
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
                ? Path.Combine(VoxPaths.UserData, PronunciationRule.FolderName)
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
                // Add-ons' announcement filters: on words, before symbols are named (read when
                // speaking, as the add-on manager is made after the speech queue)
                new AddOnTextRule(() => sp.GetRequiredService<AddOnManager>().Filters),
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
                    KeyMap.ReadUserBindings(policy.AllowUserProfileAccess ? KeyMap.DefaultUserKeyMapPath : null)),
                // NVDA's or JAWS's settings are offered when found (never on secure screens)
                policy.AllowSettingsWrites && policy.AllowUserProfileAccess ? sp.GetRequiredService<SettingsImportCommands>() : null);
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
        // Custom labels for web pages' elements, kept per site; none on secure screens
        services.AddSingleton<CustomLabelStore>(sp =>
        {
            var policy = sp.GetService<RunPolicy>() ?? RunPolicy.Normal;
            return new CustomLabelStore(
                policy.AllowSettingsWrites && policy.AllowUserProfileAccess ? Path.Combine(VoxPaths.UserData, CustomLabelStore.FileName) : null,
                sp.GetRequiredService<ILogger<CustomLabelStore>>());
        });
        services.AddSingleton<ICustomLabels>(sp => sp.GetRequiredService<CustomLabelStore>());
        services.AddSingleton<ICustomLabelsPresenter, CustomLabelsPresenter>();
        services.AddSingleton<CustomLabelCommands>(sp =>
        {
            var tracker = sp.GetRequiredService<BrowseDocumentTracker>();
            return new CustomLabelCommands(
                sp.GetRequiredService<CustomLabelStore>(),
                sp.GetRequiredService<BrowseModeController>(),
                sp.GetRequiredService<ITextPrompt>(),
                sp.GetRequiredService<ICustomLabelsPresenter>(),
                sp.GetRequiredService<EventPipeline>(),
                () => tracker.RequestRecapture(null),
                sp.GetRequiredService<ILogger<CustomLabelCommands>>(),
                sp.GetRequiredService<SpeechQueue>());
        });
        // Automatic labels: unlabelled graphics and icon buttons described in the background
        services.AddSingleton<AutomaticLabeler>(sp =>
        {
            var tracker = sp.GetRequiredService<BrowseDocumentTracker>();
            return new AutomaticLabeler(
                sp.GetRequiredService<AiAssistant>(),
                sp.GetRequiredService<IScreenCapture>(),
                tracker,
                sp.GetRequiredService<IAiContextSource>(),
                sp.GetRequiredService<CustomLabelStore>(),
                sp.GetRequiredService<IOptionsMonitor<VoxSettings>>(),
                () => tracker.RequestRecapture(null),
                sp.GetRequiredService<ILogger<AutomaticLabeler>>());
        });
        // Settings from NVDA and JAWS (Vox menu, first-run wizard); none on secure screens
        services.AddSingleton<ISettingsImportPresenter, SettingsImportPresenter>();
        services.AddSingleton<SettingsImportCommands>(sp =>
        {
            var policy = sp.GetService<RunPolicy>() ?? RunPolicy.Normal;
            var settings = sp.GetRequiredService<IOptionsMonitor<VoxSettings>>();
            var dispatcher = sp.GetRequiredService<KeyInputDispatcher>();
            var keyMapLogger = sp.GetRequiredService<ILogger<KeyMap>>();
            var speech = sp.GetRequiredService<SpeechEngineRegistry>();
            var configDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "config");
            var import = policy.AllowSettingsWrites && policy.AllowUserProfileAccess
                ? new SettingsImport(ImportLocations.ForCurrentUser(policy.SettingsPath), speech.VoicesOf,
                    layout => KeyMap.LayoutBindings(configDirectory, layout))
                : null;
            return new SettingsImportCommands(
                import,
                sp.GetRequiredService<ISettingsImportPresenter>(),
                settings,
                sp.GetRequiredService<SettingsMonitor>().UpdateSettings,
                () => dispatcher.SetKeyMap(LoadKeyMap(settings.CurrentValue.KeyboardLayout, keyMapLogger, policy)),
                sp.GetRequiredService<BrowseModeController>(),
                sp.GetRequiredService<EventPipeline>(),
                sp.GetRequiredService<ILogger<SettingsImportCommands>>(),
                sp.GetRequiredService<SpeechQueue>());
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

        // Add-ons (docs/addons.md): each in its own load context, none in safe mode, on the secure
        // screens or with the DisableAddOns policy. Everything they reach is looked up when used,
        // since they reach most of Vox.
        services.AddSingleton<RestartRequest>();
        services.AddSingleton<AddOnTrustStore>(sp =>
        {
            var policy = sp.GetService<RunPolicy>() ?? RunPolicy.Normal;
            return AddOnTrustStore.Load(
                Path.Combine(AppContext.BaseDirectory, "assets", "config", AddOnTrustStore.VoxKeyFileName),
                policy.AllowUserProfileAccess ? Path.Combine(VoxPaths.UserData, "addons", "trusted-keys") : null);
        });
        services.AddSingleton<AddOnManager>(sp =>
        {
            var policy = sp.GetService<RunPolicy>() ?? RunPolicy.Normal;
            var settings = sp.GetRequiredService<IOptionsMonitor<VoxSettings>>();
            var loggers = sp.GetRequiredService<ILoggerFactory>();
            return new AddOnManager(
                policy.AllowUserProfileAccess ? Path.Combine(VoxPaths.UserData, "addons") : null,
                policy,
                new AssemblyAddOnLoader(),
                sp.GetRequiredService<AddOnTrustStore>(),
                new AddOnIntegration
                {
                    Speak = (text, priority) => sp.GetRequiredService<SpeechQueue>().Enqueue(new Utterance(text, SdkEvents.Priority(priority))),
                    StopSpeaking = () => sp.GetRequiredService<SpeechQueue>().CancelAll(),
                    Post = work => sp.GetRequiredService<IEventSink>().Post(new PipelineWorkEvent(DateTimeOffset.UtcNow, work)),
                    ForegroundProcessName = () => sp.GetRequiredService<AppModuleManager>().ActiveProcessName,
                    SetAppModules = modules => sp.GetRequiredService<AppModuleManager>().SetAddOnModules(modules),
                    ActiveAppModule = () => sp.GetRequiredService<AppModuleManager>().ActiveModule,
                    SetKeyLayer = layer => sp.GetRequiredService<KeyInputDispatcher>().SetAddOnKeys(layer),
                    SpeechEngines = sp.GetRequiredService<SpeechEngineRegistry>(),
                    SelectedSpeechEngine = () => settings.CurrentValue.SpeechEngine,
                    Braille = sp.GetRequiredService<BrailleDisplayManager>(),
                    // A label pack's labels apply to the page being browsed at once
                    SetLabelPack = (id, labels) =>
                    {
                        var store = sp.GetRequiredService<CustomLabelStore>();
                        if (labels is null)
                            store.RemovePack(id);
                        else
                            store.SetPack(id, labels);
                        sp.GetRequiredService<BrowseDocumentTracker>().RequestRecapture(null);
                    },
                },
                loggers.CreateLogger,
                sp.GetRequiredService<ILogger<AddOnManager>>());
        });
        services.AddSingleton<AddOnStore>(sp =>
        {
            var policy = sp.GetService<RunPolicy>() ?? RunPolicy.Normal;
            var settings = sp.GetRequiredService<IOptionsMonitor<VoxSettings>>();
            return new AddOnStore(
                // Refuses every request while the network is off (a catalogue on this computer still works)
                NetworkGate.CreateHttpClient(policy, TimeSpan.FromSeconds(60)),
                () => Uri.TryCreate(settings.CurrentValue.AddOnCatalogueUrl, UriKind.Absolute, out var url) ? url : AddOnStore.DefaultCatalogueUrl,
                sp.GetRequiredService<AddOnTrustStore>(),
                policy,
                sp.GetRequiredService<ILogger<AddOnStore>>());
        });
        services.AddSingleton<IAddOnsPresenter, AddOnsPresenter>();
        services.AddSingleton<IAddOnStorePresenter, AddOnStorePresenter>();
        services.AddSingleton<AddOnCommands>(sp => new AddOnCommands(
            sp.GetRequiredService<AddOnManager>(),
            sp.GetRequiredService<AddOnStore>(),
            sp.GetRequiredService<IAddOnsPresenter>(),
            sp.GetRequiredService<IAddOnStorePresenter>(),
            sp.GetRequiredService<IConfirmPresenter>(),
            sp.GetRequiredService<OwnDialogRunner>(),
            sp.GetRequiredService<IEventSink>(),
            sp.GetRequiredService<SpeechQueue>(),
            sp.GetRequiredService<RestartRequest>(),
            sp.GetRequiredService<IHostApplicationLifetime>().StopApplication,
            sp.GetRequiredService<ILogger<AddOnCommands>>()));

        // Remote access (docs/remote.md): never on the secure screens or with the network off
        services.AddSingleton<RemoteAccessSession>(sp =>
        {
            var settings = sp.GetRequiredService<IOptionsMonitor<VoxSettings>>();
            var dispatcher = sp.GetRequiredService<KeyInputDispatcher>();
            var policy = sp.GetService<RunPolicy>() ?? RunPolicy.Normal;
            return new RemoteAccessSession(
                new RemoteTransport(policy),
                sp.GetRequiredService<SpeechQueue>(),
                dispatcher,
                new RemoteKeyPlayer(sp.GetRequiredService<SendInputKeyboardOutput>(), () => settings.CurrentValue.ModifierKey),
                () => KeyNameOf(dispatcher, NavigationCommand.ToggleRemoteControl, settings.CurrentValue.ModifierKey),
                sp.GetRequiredService<ILogger<RemoteAccessSession>>())
            {
                // The controlled side sends its braille line; the controlling side shows the remote one
                Braille = policy.IsSecure ? null : sp.GetRequiredService<BrailleController>(),
            };
        });
        services.AddSingleton<IRemotePresenter, RemotePresenter>();
        services.AddSingleton<RemoteCommands>(sp => new RemoteCommands(
            sp.GetRequiredService<RemoteAccessSession>(),
            sp.GetRequiredService<IRemotePresenter>(),
            sp.GetRequiredService<IConfirmPresenter>(),
            sp.GetRequiredService<OwnDialogRunner>(),
            sp.GetRequiredService<IClipboard>(),
            sp.GetRequiredService<SpeechQueue>(),
            sp.GetRequiredService<IOptionsMonitor<VoxSettings>>(),
            sp.GetRequiredService<SettingsMonitor>().UpdateSettings,
            sp.GetService<RunPolicy>() ?? RunPolicy.Normal,
            sp.GetRequiredService<ILogger<RemoteCommands>>()));

        // Braille: display drivers in the order auto-detect tries them, then the controller
        services.AddSingleton<SendInputKeyboardOutput>();
        services.AddSingleton<IKeyboardOutput>(sp => sp.GetRequiredService<SendInputKeyboardOutput>());
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

    /// <summary>The key <paramref name="command"/> is on in the dispatcher's keymap ("Insert+F11"), or the command's name.</summary>
    public static string KeyNameOf(KeyInputDispatcher dispatcher, NavigationCommand command, ModifierKey modifierKey)
    {
        var key = dispatcher.KeyMap.Bindings.Where(b => b.Command == command).Select(b => b.Key).Cast<KeyMapKey?>().FirstOrDefault();
        return key is { } found
            ? InputHelp.KeyName(found.Modifiers, found.VkCode, screenReaderKey: InputHelp.ScreenReaderKeyName(modifierKey))
            : CommandCatalog.Describe(command).Name;
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
