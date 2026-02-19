using Vox.Core.Audio;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.App;

public static class ServiceRegistration
{
    public static void RegisterServices(HostBuilderContext context, IServiceCollection services)
    {
        // Speech
        services.AddSingleton<ISpeechEngine, SapiSpeechEngine>();
        services.AddSingleton<SpeechQueue>();

        // Audio
        services.AddSingleton<IAudioCuePlayer, AudioCuePlayer>();

        // Pipeline
        services.AddSingleton<EventPipeline>();

        // Hosted service
        services.AddHostedService<ScreenReaderService>();
    }
}
