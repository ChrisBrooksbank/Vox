using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

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
        services.AddSingleton<UIAProvider>();
        services.AddSingleton<UIAEventSubscriber>();
        services.AddSingleton<LiveRegionMonitor>();

        // Input
        services.AddSingleton<IKeyboardHook, KeyboardHook>();
        services.AddSingleton<KeyMap>(sp =>
        {
            var keyMapPath = Path.Combine(
                AppContext.BaseDirectory,
                "assets", "config", "default-keymap.json");
            return KeyMap.LoadFromFile(keyMapPath);
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
            return new TypingEchoHandler(pipeline, () => settings.CurrentValue.TypingEchoMode, logger);
        });

        // First-run wizard
        services.AddSingleton<FirstRunWizard>();

        // Navigation
        services.AddSingleton<NavigationManager>();
        services.AddSingleton<QuickNavHandler>();
        services.AddSingleton<AnnouncementBuilder>();
        services.AddSingleton<SayAllController>();

        // Hosted service
        services.AddHostedService<ScreenReaderService>();
    }
}
