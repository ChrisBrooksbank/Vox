using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace Vox.Core.AppModules.Meetings;

/// <summary><see cref="IMicrophone"/> over the default capture endpoint's <c>IAudioEndpointVolume</c>.</summary>
public sealed class CoreAudioMicrophone(ILogger<CoreAudioMicrophone>? logger = null) : IMicrophone
{
    public bool? IsMuted()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            return device.AudioEndpointVolume.Mute;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Could not read the microphone's mute");
            return null;
        }
    }

    public bool SetMuted(bool muted)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            device.AudioEndpointVolume.Mute = muted;
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Could not set the microphone's mute");
            return false;
        }
    }
}
