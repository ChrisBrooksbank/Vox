using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Vox.Core.Audio;

/// <summary>
/// Audio output devices, and opening one through WASAPI (shared mode, low latency), with the
/// default device and then waveOut as fallbacks.
/// </summary>
public static class AudioOutputDevices
{
    private const int LatencyMs = 60;

    /// <summary>Names of the active output devices.</summary>
    public static IReadOnlyList<string> List()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(d => d.FriendlyName)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Plays <paramref name="source"/> on the device named <paramref name="deviceName"/> (null or
    /// not found: the default device). Returns the started output.
    /// </summary>
    public static IDisposable Open(ISampleProvider source, string? deviceName)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = Choose(enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList(), deviceName)
                ?? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var output = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, LatencyMs);
            output.Init(source);
            output.Play();
            return output;
        }
        catch
        {
            // No WASAPI (or the device vanished): the classic path
            var output = new WaveOutEvent { DesiredLatency = 100 };
            output.Init(source);
            output.Play();
            return output;
        }
    }

    /// <summary>The device with this name (exactly, then ignoring case), or null.</summary>
    public static T? Choose<T>(IReadOnlyList<T> devices, string? name, Func<T, string>? nameOf = null) where T : class
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        nameOf ??= d => d is MMDevice mm ? mm.FriendlyName : d.ToString() ?? string.Empty;
        return devices.FirstOrDefault(d => nameOf(d) == name)
            ?? devices.FirstOrDefault(d => string.Equals(nameOf(d), name, StringComparison.OrdinalIgnoreCase));
    }
}
