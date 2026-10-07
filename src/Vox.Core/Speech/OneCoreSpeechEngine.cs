using Microsoft.Extensions.Logging;
using NAudio.Wave;
using Vox.Core.Audio;

namespace Vox.Core.Speech;

/// <summary>A OneCore voice: its display name and BCP 47 language.</summary>
public sealed record OneCoreVoice(string Name, string Language);

/// <summary>How to synthesize: the voice (null: the default), rate (1 = normal), pitch (0–2, 1 normal) and volume (0–1).</summary>
public sealed record OneCoreOptions(string? Voice, double SpeakingRate, double Pitch, double Volume);

/// <summary>The OneCore synthesizer (Windows.Media.SpeechSynthesis), behind a seam for tests.</summary>
public interface IOneCoreSynthesizer
{
    IReadOnlyList<OneCoreVoice> Voices { get; }

    /// <summary>The voice used when none is chosen.</summary>
    string? DefaultVoice { get; }

    /// <summary>Synthesizes <paramref name="text"/> into a WAV file's bytes.</summary>
    Task<byte[]> SynthesizeAsync(string text, OneCoreOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// Speech through the OneCore synthesizer (Windows.Media.SpeechSynthesis, including natural voices
/// where installed). Each utterance is synthesized to audio and played through
/// <see cref="IAudioStreamPlayer"/>, so speech uses the same output device as the earcons.
/// Cancelling stops the audio at once; an utterance still being synthesized is dropped.
/// </summary>
public sealed class OneCoreSpeechEngine : ISpeechEngine, IDisposable
{
    /// <summary>Words per minute at speaking rate 1.</summary>
    public const double NormalWpm = 180.0;
    public const double MinSpeakingRate = 0.5;
    public const double MaxSpeakingRate = 6.0;

    private readonly IOneCoreSynthesizer _synthesizer;
    private readonly IAudioStreamPlayer _player;
    private readonly ILogger<OneCoreSpeechEngine> _logger;
    private readonly object _lock = new();
    // Raised by Cancel: anything started before it is dropped
    private long _epoch;
    private Playback? _current;
    private volatile string? _voice;
    private double _speakingRate = 1.0;
    private int _pitch = ISpeechEngine.DefaultPitch;
    private int _volume = 100;

    public OneCoreSpeechEngine(IOneCoreSynthesizer synthesizer, IAudioStreamPlayer player, ILogger<OneCoreSpeechEngine> logger)
    {
        _synthesizer = synthesizer;
        _player = player;
        _logger = logger;
    }

    public bool IsSpeaking
    {
        get { lock (_lock) return _current is not null; }
    }

    public SpeechCapabilities Capabilities => SpeechCapabilities.Pitch | SpeechCapabilities.Volume;

    // OneCore speaks up to six times its normal speed itself: the full range Vox offers
    public int MaxRateWpm => ISpeechEngine.MaxSupportedWpm;

    public async Task SpeakAsync(Utterance utterance, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(utterance.Text))
            return;
        if (utterance.Priority == SpeechPriority.Interrupt)
            Cancel();
        cancellationToken.ThrowIfCancellationRequested();

        long epoch = Interlocked.Read(ref _epoch);
        var options = Options();
        var wav = await _synthesizer.SynthesizeAsync(utterance.Text, options, cancellationToken).ConfigureAwait(false);

        var playback = new Playback(new WaveFileReader(new MemoryStream(wav)).ToSampleProvider());
        lock (_lock)
        {
            // Cancelled while it was being synthesized
            if (epoch != Interlocked.Read(ref _epoch))
                throw new OperationCanceledException();
            cancellationToken.ThrowIfCancellationRequested();
            _current?.Stop();
            _current = playback;
        }

        using var registration = cancellationToken.Register(playback.Stop);
        try
        {
            _player.PlayStream(playback);
            await playback.Completion.ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
            {
                if (_current == playback)
                    _current = null;
            }
        }
    }

    public void Cancel()
    {
        Playback? current;
        lock (_lock)
        {
            Interlocked.Increment(ref _epoch);
            current = _current;
            _current = null;
        }
        current?.Stop();
    }

    /// <summary>Speaking rate (1 = normal) for a rate in words per minute.</summary>
    public static double WpmToSpeakingRate(int wpm) =>
        Math.Clamp(Math.Min(wpm, ISpeechEngine.MaxSupportedWpm) / NormalWpm, MinSpeakingRate, MaxSpeakingRate);

    public void SetRate(int wpm)
    {
        lock (_lock) _speakingRate = WpmToSpeakingRate(wpm);
    }

    public void SetPitch(int pitch)
    {
        lock (_lock) _pitch = Math.Clamp(pitch, 0, 100);
    }

    public void SetVolume(int volume)
    {
        lock (_lock) _volume = Math.Clamp(volume, 0, 100);
    }

    /// <summary>Selects a voice by name; empty (or a voice that isn't installed) selects the default voice.</summary>
    public void SetVoice(string voiceName)
    {
        if (string.IsNullOrWhiteSpace(voiceName))
        {
            _voice = null;
            return;
        }
        if (_synthesizer.Voices.Any(v => v.Name == voiceName))
        {
            _voice = voiceName;
            _logger.LogInformation("Voice set to {VoiceName}", voiceName);
        }
        else
        {
            _logger.LogWarning("Voice {VoiceName} is not installed; keeping the current voice", voiceName);
        }
    }

    public string? CurrentVoice => _voice ?? _synthesizer.DefaultVoice;

    public IReadOnlyList<string> GetAvailableVoices() => _synthesizer.Voices.Select(v => v.Name).ToList();

    public IReadOnlyList<string> GetLanguages() =>
        _synthesizer.Voices.Select(v => v.Language).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Synthesizes a word in the background so the first real utterance doesn't wait for the
    /// synthesizer to load its voice.
    /// </summary>
    public void WarmUp() => _ = WarmUpAsync();

    private async Task WarmUpAsync()
    {
        try { await _synthesizer.SynthesizeAsync("ready", Options(), CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogDebug(ex, "OneCore warm-up failed"); }
    }

    public void Dispose()
    {
        Cancel();
        (_synthesizer as IDisposable)?.Dispose();
    }

    private OneCoreOptions Options()
    {
        lock (_lock)
            return new OneCoreOptions(_voice, _speakingRate, _pitch / 50.0, _volume / 100.0);
    }

    /// <summary>
    /// An utterance's audio as a mixer input: it completes <see cref="Completion"/> when it has
    /// all been played, and <see cref="Stop"/> silences it at once (cancelling the completion).
    /// </summary>
    private sealed class Playback(ISampleProvider source) : ISampleProvider
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _stopped;

        public Task Completion => _completion.Task;

        public WaveFormat WaveFormat => source.WaveFormat;

        public void Stop()
        {
            _stopped = true;
            _completion.TrySetCanceled();
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if (_stopped)
                return 0;
            int read;
            try
            {
                read = source.Read(buffer, offset, count);
            }
            catch (Exception ex)
            {
                _completion.TrySetException(ex);
                return 0;
            }
            // The mixer drops an input that returns less than it asked for
            if (read < count)
                _completion.TrySetResult();
            return read;
        }
    }
}
