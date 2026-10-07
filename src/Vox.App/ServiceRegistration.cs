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
        services.AddSingleton<ISpeechEngine, SapiSpeechEngine>();
        services.AddSingleton<SpeechQueue>(sp =>
        {
            var queue = new SpeechQueue(sp.GetRequiredService<ISpeechEngine>(), sp.GetRequiredService<ILogger<SpeechQueue>>());
            var history = sp.GetRequiredService<SpeechHistory>();
            var latency = sp.GetRequiredService<LatencyTracker>();
            queue.UtteranceStarted += (_, u) => history.Add(u.Text);
            queue.UtteranceStarted += (_, _) => latency.NoteSpeechStarted();
            queue.QueueLatency = wait => latency.Record(LatencyTracker.QueueToEngine, wait);
            return queue;
        });
        services.AddSingleton<SpeechHistory>(_ => new SpeechHistory());
        services.AddSingleton<LatencyTracker>(sp =>
        {
            var tracker = new LatencyTracker();
            tracker.StartReporting(sp.GetRequiredService<ILogger<LatencyTracker>>(), TimeSpan.FromMinutes(1));
            return tracker;
        });
        services.AddSingleton<SpeechViewer>();

        // Audio
        services.AddSingleton<IAudioCuePlayer, AudioCuePlayer>();

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
            var tracker = new TextCaretTracker(spellingErrors: () => settings.CurrentValue.SpellingErrors);
            var cues = sp.GetRequiredService<IAudioCuePlayer>();
            tracker.SpellingErrorEntered += (_, _) => cues.Play("error");
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
        services.AddSingleton<KeyMap>(sp =>
        {
            var keyMapPath = Path.Combine(
                AppContext.BaseDirectory,
                "assets", "config", "default-keymap.json");
            var keyMap = KeyMap.LoadFromFileOrBuiltIn(keyMapPath, out var error, out var warnings);
            var logger = sp.GetRequiredService<ILogger<KeyMap>>();
            if (error is not null)
            {
                logger.LogError(error,
                    "Could not load keymap from {Path}; using the built-in default keymap", keyMapPath);
            }
            foreach (var warning in warnings)
                logger.LogWarning("Keymap: {Warning}", warning);
            return keyMap;
        });
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
        services.AddSingleton<AnnouncementBuilder>();
        services.AddSingleton<SayAllController>();
        services.AddSingleton<IElementsListPresenter, ElementsListPresenter>();
        services.AddSingleton<BrowseModeController>();

        // Hosted service
        services.AddHostedService<ScreenReaderService>();
    }
}
