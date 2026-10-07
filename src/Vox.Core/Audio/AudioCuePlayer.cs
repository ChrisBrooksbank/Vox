using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Vox.Core.Audio;

/// <summary>
/// NAudio-based audio cue player with pre-loaded CachedSound objects.
/// Fire-and-forget playback that does not block speech.
/// Sounds come from an earcon scheme: a folder under assets/sounds with a manifest.json naming
/// each cue's file (<see cref="Scheme"/>); cues a scheme leaves out play the default scheme's.
/// </summary>
public sealed class AudioCuePlayer : IAudioCuePlayer, IAudioStreamPlayer, IDisposable
{
    private readonly ILogger<AudioCuePlayer> _logger;
    private readonly Dictionary<string, CachedSound?> _sounds = new();
    private readonly string _soundsDirectory;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (!value)
                ResetOutput(); // no open audio stream while cues are off
        }
    }
    private volatile bool _isEnabled = true;

    /// <summary>
    /// How long the output stays open after the last cue. An open stream (even of silence) keeps
    /// Windows from sleeping, so it is closed when idle and re-opened by the next cue.
    /// </summary>
    public TimeSpan IdleClose { get; set; } = TimeSpan.FromSeconds(5);

    // Opens and starts an output device playing the mixer; disposing it closes the device
    private readonly Func<ISampleProvider, IDisposable> _outputFactory;
    private readonly System.Threading.Timer _idleTimer;

    /// <summary>Every cue Vox plays; the default scheme has a sound for each.</summary>
    public static readonly IReadOnlyList<string> Cues = new[]
    {
        "browse_mode",
        "focus_mode",
        "boundary",
        "wrap",
        "error",
        "list_entry",
        "list_exit",
        "table_entry",
        "table_exit",
        "landmark",
        "clickable",
        "progress",
    };

    /// <summary>The scheme every other scheme falls back to.</summary>
    public const string DefaultScheme = "default";

    private string _scheme = DefaultScheme;

    /// <summary>
    /// The earcon scheme (a folder under the sounds directory). Changing it loads its sounds; an
    /// unknown scheme plays the default one.
    /// </summary>
    public string Scheme
    {
        get => _scheme;
        set
        {
            var scheme = string.IsNullOrWhiteSpace(value) ? DefaultScheme : value;
            if (scheme == _scheme)
                return;
            _scheme = scheme;
            PreloadSounds();
        }
    }

    public AudioCuePlayer(ILogger<AudioCuePlayer> logger, string? soundsDirectory = null)
        : this(logger, soundsDirectory, outputFactory: null)
    {
    }

    /// <param name="outputFactory">
    /// Opens an output device playing the given mixer (a test seam); defaults to WASAPI on
    /// <see cref="OutputDevice"/> (<see cref="AudioOutputDevices.Open"/>).
    /// </param>
    public AudioCuePlayer(ILogger<AudioCuePlayer> logger, string? soundsDirectory, Func<ISampleProvider, IDisposable>? outputFactory)
    {
        _logger = logger;
        _soundsDirectory = soundsDirectory ?? GetDefaultSoundsDirectory();
        _outputFactory = outputFactory ?? (mixer => AudioOutputDevices.Open(mixer, OutputDevice));
        _idleTimer = new System.Threading.Timer(_ => CloseIfIdle(), null, Timeout.Infinite, Timeout.Infinite);
        PreloadSounds();
    }

    private string? _outputDevice;

    /// <summary>
    /// The output device's name (null: the default device). Changing it reopens the output on the
    /// new device with the next cue.
    /// </summary>
    public string? OutputDevice
    {
        get => _outputDevice;
        set
        {
            if (_outputDevice == value)
                return;
            _outputDevice = value;
            ResetOutput();
            if (_keepAwake)
                OpenSilently();
        }
    }

    private volatile bool _keepAwake;

    /// <summary>
    /// Keeps the output device open, playing silence, instead of closing it when idle: some
    /// devices (Bluetooth, HDMI) take a moment to wake and clip the start of the next sound, and an
    /// active device keeps speech on the same device from being clipped too.
    /// </summary>
    public bool KeepAwake
    {
        get => _keepAwake;
        set
        {
            _keepAwake = value;
            if (value)
                OpenSilently();
            else
                lock (_outputLock) RestartIdleTimer();
        }
    }

    private void OpenSilently()
    {
        try
        {
            lock (_outputLock)
            {
                _idleTimer.Change(Timeout.Infinite, Timeout.Infinite);
                EnsureMixer();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not open the audio output");
        }
    }

    /// <summary>True while an output device is open (for tests).</summary>
    public bool IsOutputOpen
    {
        get { lock (_outputLock) return _output is not null; }
    }

    /// <summary>Plays a cue already converted to <see cref="MixFormat"/> (for tests).</summary>
    public void PlayProvider(ISampleProvider provider)
    {
        if (!IsEnabled)
            return;
        AddToMixer(provider);
    }

    /// <summary>
    /// Adds a cue to the mixer, opening the output if needed. Done under the output lock with the
    /// idle timer stopped, so the idle close can't discard the mixer between the two steps.
    /// </summary>
    private void AddToMixer(ISampleProvider provider)
    {
        lock (_outputLock)
        {
            _idleTimer.Change(Timeout.Infinite, Timeout.Infinite);
            EnsureMixer().AddMixerInput(provider);
            _lastCueTick = Environment.TickCount64;
            RestartIdleTimer();
        }
    }

    // How often the idle timer looks again while a stream is still playing
    private const long StreamCheckIntervalMs = 1000;

    // When the last cue was added (guarded by _outputLock)
    private long _lastCueTick;

    /// <summary>
    /// Closes the output unless a cue was added within <see cref="IdleClose"/>. Run by the idle
    /// timer: stopping the timer can't recall a callback that has already fired and is waiting
    /// for the lock, which would otherwise close the device under the cue just added.
    /// </summary>
    public void CloseIfIdle()
    {
        lock (_outputLock)
        {
            if (_keepAwake)
                return;
            long remaining = (long)IdleClose.TotalMilliseconds - (Environment.TickCount64 - _lastCueTick);
            // A stream (a long utterance) still playing: look again later
            if (_mixer is not null && remaining <= 0 && _mixer.MixerInputs.Any(i => i is StreamInput))
                remaining = Math.Max((long)IdleClose.TotalMilliseconds, StreamCheckIntervalMs);
            if (_mixer is not null && remaining > 0)
            {
                // A cue was added since: wait out its idle time (the timer's clock and the tick
                // count can also disagree by a few ms)
                try { _idleTimer.Change(remaining, Timeout.Infinite); } catch (ObjectDisposedException) { }
                return;
            }
            ResetOutput();
        }
    }

    public void Play(string cueName)
    {
        if (!IsEnabled)
            return;

        CachedSound? sound;
        lock (_sounds)
            _sounds.TryGetValue(cueName, out sound);
        if (sound is null)
        {
            _logger.LogDebug("Audio cue not found or not loaded: {CueName}", cueName);
            return;
        }

        try
        {
            // One output device stays open, feeding a mixer: a cue is just another mixer input,
            // so it starts without opening a device and overlapping cues simply mix
            AddToMixer(ToMixFormat(new CachedSoundSampleProvider(sound)));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error playing audio cue: {CueName}", cueName);
            ResetOutput(); // try a fresh device next time (e.g. the audio device changed)
        }
    }

    /// <summary>
    /// Plays a stream (speech) on the same output as the cues, whether or not cues are enabled.
    /// </summary>
    public void PlayStream(ISampleProvider source)
    {
        try
        {
            AddToMixer(new StreamInput(ToMixFormat(source)));
        }
        catch
        {
            ResetOutput(); // try a fresh device next time
            throw;
        }
    }

    public void PlayTone(double frequencyHz, int durationMs)
    {
        if (!IsEnabled || frequencyHz <= 0 || durationMs <= 0)
            return;
        try
        {
            var tone = new SignalGenerator(MixFormat.SampleRate, MixFormat.Channels)
            {
                Type = SignalGeneratorType.Sin,
                Frequency = frequencyHz,
                Gain = 0.15,
            };
            AddToMixer(tone.Take(TimeSpan.FromMilliseconds(durationMs)));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error playing a tone");
            ResetOutput();
        }
    }

    /// <summary>A stream in the mixer, which keeps the output open until it ends.</summary>
    private sealed class StreamInput(ISampleProvider source) : ISampleProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;
        public int Read(float[] buffer, int offset, int count) => source.Read(buffer, offset, count);
    }

    /// <summary>The mixer's format: every cue is converted to it.</summary>
    public static readonly WaveFormat MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);

    private readonly object _outputLock = new();
    private IDisposable? _output;
    private MixingSampleProvider? _mixer;

    private MixingSampleProvider EnsureMixer()
    {
        lock (_outputLock)
        {
            if (_mixer is not null)
                return _mixer;

            // ReadFully keeps the device playing silence between cues, so quick cues don't
            // re-open it; the idle timer closes it once cues stop
            var mixer = new MixingSampleProvider(MixFormat) { ReadFully = true };
            _output = _outputFactory(mixer);
            _mixer = mixer;
            return mixer;
        }
    }

    // Close the device IdleClose after the last cue (cues are short, so it has finished by then)
    private void RestartIdleTimer()
    {
        var delay = IdleClose;
        _idleTimer.Change(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, Timeout.InfiniteTimeSpan);
    }

    private void ResetOutput()
    {
        lock (_outputLock)
        {
            try { _output?.Dispose(); } catch { /* already gone */ }
            _output = null;
            _mixer = null;
        }
    }

    /// <summary>Converts a cue to the mixer's format (mono, 44.1 kHz), whatever its file was.</summary>
    public static ISampleProvider ToMixFormat(ISampleProvider source)
    {
        var provider = source;
        if (provider.WaveFormat.Channels == 2)
            provider = new NAudio.Wave.SampleProviders.StereoToMonoSampleProvider(provider);
        else if (provider.WaveFormat.Channels != 1)
            throw new NotSupportedException($"Audio cues must be mono or stereo, not {provider.WaveFormat.Channels} channels");
        if (provider.WaveFormat.SampleRate != MixFormat.SampleRate)
            provider = new WdlResamplingSampleProvider(provider, MixFormat.SampleRate);
        return provider;
    }

    private void PreloadSounds()
    {
        var files = SchemeFiles(_soundsDirectory, _scheme, _logger);
        var sounds = new Dictionary<string, CachedSound?>();
        foreach (var name in Cues)
        {
            if (!files.TryGetValue(name, out var path) || !File.Exists(path))
            {
                _logger.LogDebug("No sound for audio cue {CueName}", name);
                sounds[name] = null;
                continue;
            }
            try
            {
                sounds[name] = new CachedSound(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load audio cue: {CueName} from {Path}", name, path);
                sounds[name] = null;
            }
        }
        lock (_sounds)
        {
            _sounds.Clear();
            foreach (var (name, sound) in sounds)
                _sounds[name] = sound;
        }
    }

    /// <summary>
    /// Each cue's sound file in <paramref name="scheme"/>: the files its manifest names, with the
    /// default scheme's for the cues it leaves out (or all of them when it has no manifest).
    /// </summary>
    public static IReadOnlyDictionary<string, string> SchemeFiles(string soundsDirectory, string scheme, ILogger? logger = null)
    {
        var files = ReadManifest(soundsDirectory, DefaultScheme, logger);
        if (!string.Equals(scheme, DefaultScheme, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var (cue, path) in ReadManifest(soundsDirectory, scheme, logger))
                files[cue] = path;
        }
        return files;
    }

    private sealed class Manifest
    {
        [System.Text.Json.Serialization.JsonPropertyName("cues")]
        public Dictionary<string, string> Cues { get; set; } = new();
    }

    private static Dictionary<string, string> ReadManifest(string soundsDirectory, string scheme, ILogger? logger)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // A scheme is a folder name, never a path
        if (scheme.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || scheme is "." or "..")
            return files;
        var folder = Path.Combine(soundsDirectory, scheme);
        var path = Path.Combine(folder, "manifest.json");
        try
        {
            if (!File.Exists(path))
                return files;
            var manifest = System.Text.Json.JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path),
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
            foreach (var (cue, file) in manifest?.Cues ?? [])
            {
                // Files stay inside the scheme's folder
                if (!string.IsNullOrWhiteSpace(file) && file.IndexOfAny(['/', '\\']) < 0 && !file.Contains("..", StringComparison.Ordinal))
                    files[cue] = Path.Combine(folder, file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            logger?.LogWarning(ex, "Could not read the earcon scheme {Scheme}", scheme);
        }
        return files;
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
        // CachedSound doesn't implement IDisposable in NAudio 2.x; only the output device needs closing
        _idleTimer.Dispose();
        ResetOutput();
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
