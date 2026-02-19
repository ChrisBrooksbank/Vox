using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace Vox.Core.Audio;

/// <summary>
/// NAudio-based audio cue player with pre-loaded CachedSound objects.
/// Fire-and-forget playback that does not block speech.
/// Phase 1 sounds: browse_mode, focus_mode, boundary, wrap, error.
/// </summary>
public sealed class AudioCuePlayer : IAudioCuePlayer, IDisposable
{
    private readonly ILogger<AudioCuePlayer> _logger;
    private readonly Dictionary<string, CachedSound?> _sounds = new();
    private readonly string _soundsDirectory;

    public bool IsEnabled { get; set; } = true;

    public static readonly IReadOnlyList<string> Phase1Sounds = new[]
    {
        "browse_mode",
        "focus_mode",
        "boundary",
        "wrap",
        "error"
    };

    public AudioCuePlayer(ILogger<AudioCuePlayer> logger, string? soundsDirectory = null)
    {
        _logger = logger;
        _soundsDirectory = soundsDirectory ?? GetDefaultSoundsDirectory();
        PreloadSounds();
    }

    public void Play(string cueName)
    {
        if (!IsEnabled)
            return;

        if (!_sounds.TryGetValue(cueName, out var sound) || sound == null)
        {
            _logger.LogDebug("Audio cue not found or not loaded: {CueName}", cueName);
            return;
        }

        // Fire-and-forget: play on thread pool to not block caller
        _ = Task.Run(() => PlaySound(sound, cueName));
    }

    private void PlaySound(CachedSound sound, string cueName)
    {
        try
        {
            using var output = new WaveOutEvent();
            var provider = new CachedSoundSampleProvider(sound);
            output.Init(provider);
            output.Play();

            // Wait for playback to complete (with timeout)
            var timeout = DateTime.UtcNow.AddSeconds(5);
            while (output.PlaybackState == PlaybackState.Playing && DateTime.UtcNow < timeout)
            {
                Thread.Sleep(10);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error playing audio cue: {CueName}", cueName);
        }
    }

    private void PreloadSounds()
    {
        foreach (var name in Phase1Sounds)
        {
            var path = Path.Combine(_soundsDirectory, $"{name}.wav");
            if (File.Exists(path))
            {
                try
                {
                    _sounds[name] = new CachedSound(path);
                    _logger.LogDebug("Loaded audio cue: {CueName}", name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load audio cue: {CueName} from {Path}", name, path);
                    _sounds[name] = null;
                }
            }
            else
            {
                _logger.LogDebug("Audio cue file not found: {Path}", path);
                _sounds[name] = null;
            }
        }
    }

    private static string GetDefaultSoundsDirectory()
    {
        // Look for sounds relative to the executable
        var exeDir = AppContext.BaseDirectory;
        var assetsPath = Path.Combine(exeDir, "assets", "sounds");
        if (Directory.Exists(assetsPath))
            return assetsPath;

        // Fallback: look up from solution root
        var dir = exeDir;
        for (int i = 0; i < 6; i++)
        {
            var candidate = Path.Combine(dir ?? "", "assets", "sounds");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        return Path.Combine(exeDir, "assets", "sounds");
    }

    public void Dispose()
    {
        // CachedSound doesn't implement IDisposable in NAudio 2.x
    }
}

/// <summary>
/// Caches audio data in memory for fast repeated playback.
/// </summary>
public class CachedSound
{
    public float[] AudioData { get; }
    public WaveFormat WaveFormat { get; }

    public CachedSound(string audioFileName)
    {
        using var audioFileReader = new AudioFileReader(audioFileName);
        WaveFormat = audioFileReader.WaveFormat;
        var wholeFile = new List<float>((int)(audioFileReader.Length / 4));
        var buffer = new float[audioFileReader.WaveFormat.SampleRate * audioFileReader.WaveFormat.Channels];
        int samplesRead;
        while ((samplesRead = audioFileReader.Read(buffer, 0, buffer.Length)) > 0)
        {
            wholeFile.AddRange(buffer.Take(samplesRead));
        }
        AudioData = wholeFile.ToArray();
    }
}

/// <summary>
/// ISampleProvider backed by a CachedSound.
/// </summary>
public class CachedSoundSampleProvider : ISampleProvider
{
    private readonly CachedSound _cachedSound;
    private int _position;

    public CachedSoundSampleProvider(CachedSound cachedSound)
    {
        _cachedSound = cachedSound;
    }

    public WaveFormat WaveFormat => _cachedSound.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var availableSamples = _cachedSound.AudioData.Length - _position;
        var samplesToCopy = Math.Min(availableSamples, count);
        Array.Copy(_cachedSound.AudioData, _position, buffer, offset, samplesToCopy);
        _position += samplesToCopy;
        return samplesToCopy;
    }
}
