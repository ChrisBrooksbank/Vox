using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Input;
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
            return new SettingsManager(logger, defaultSettingsPath);
        });
        services.AddSingleton<SettingsMonitor>();
        services.AddSingleton<IOptionsMonitor<VoxSettings>>(sp => sp.GetRequiredService<SettingsMonitor>());

        // Speech
        services.AddSingleton<ISpeechEngine, SapiSpeechEngine>();
        services.AddSingleton<SpeechQueue>();

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
        services.AddSingleton<TextCaretTracker>(_ => new TextCaretTracker());
        services.AddSingleton<IFocusedTextSource, UIAFocusedTextSource>();
        services.AddSingleton<FocusedTextMonitor>();
        services.AddSingleton<IFocusedTextReader>(sp => sp.GetRequiredService<FocusedTextMonitor>());
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
            return new KeyInputDispatcher(hook, keyMap, pipeline, logger);
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
