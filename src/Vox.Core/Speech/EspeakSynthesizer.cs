using NAudio.Utils;
using NAudio.Wave;

namespace Vox.Core.Speech;

/// <summary>The eSpeak NG library's API, behind a seam for tests. Not thread-safe: one caller at a time.</summary>
public interface IEspeakApi
{
    /// <summary>The sample rate of the audio it produces (16-bit mono).</summary>
    int SampleRate { get; }

    IReadOnlyList<SynthesizerVoice> ListVoices();

    void SetVoice(string name);

    /// <summary>Words per minute (80–450).</summary>
    void SetRate(int wpm);

    /// <summary>0–100, 50 normal.</summary>
    void SetPitch(int pitch);

    /// <summary>0–200, 100 normal.</summary>
    void SetVolume(int volume);

    /// <summary>
    /// Synthesizes <paramref name="text"/>, handing each block of samples to
    /// <paramref name="onSamples"/>, which returns false to stop.
    /// </summary>
    void Synthesize(string text, Func<short[], bool> onSamples);
}

/// <summary>
/// eSpeak NG (an optional, separately installed component; see <see cref="EspeakComponent"/>) as
/// an <see cref="IWaveSynthesizer"/>: it synthesizes on a worker thread, one utterance at a time,
/// and stops as soon as the utterance is cancelled.
/// </summary>
public sealed class EspeakSynthesizer : IWaveSynthesizer
{
    public const int MinWpm = 80;
    public const int MaxWpm = 450;

    private readonly IEspeakApi _api;
    private readonly object _lock = new();
    private IReadOnlyList<SynthesizerVoice>? _voices;
    private string? _voice;

    public EspeakSynthesizer(IEspeakApi api)
    {
        _api = api;
    }

    public IReadOnlyList<SynthesizerVoice> Voices
    {
        get
        {
            lock (_lock)
                return _voices ??= _api.ListVoices();
        }
    }

    public string? DefaultVoice => Voices.FirstOrDefault(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase))?.Name
        ?? Voices.FirstOrDefault()?.Name;

    public int MaxRateWpm => MaxWpm;

    public Task<byte[]> SynthesizeAsync(string text, SynthesisOptions options, CancellationToken cancellationToken) =>
        Task.Run(() => Synthesize(text, options, cancellationToken), cancellationToken);

    private byte[] Synthesize(string text, SynthesisOptions options, CancellationToken cancellationToken)
    {
        var samples = new List<short>(16_384);
        int sampleRate;
        lock (_lock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var voice = options.Voice ?? DefaultVoice;
            if (voice is not null && voice != _voice)
            {
                _api.SetVoice(voice);
                _voice = voice;
            }
            _api.SetRate(Math.Clamp((int)Math.Round(options.SpeakingRate * WaveSpeechEngine.NormalWpm), MinWpm, MaxWpm));
            _api.SetPitch(Math.Clamp((int)Math.Round(options.Pitch * 50), 0, 100));
            _api.SetVolume(Math.Clamp((int)Math.Round(options.Volume * 100), 0, 200));
            _api.Synthesize(text, block =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return false;
                samples.AddRange(block);
                return true;
            });
            sampleRate = _api.SampleRate;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Wave(samples, sampleRate);
    }

    /// <summary>A WAV file of 16-bit mono <paramref name="samples"/>.</summary>
    public static byte[] Wave(IReadOnlyList<short> samples, int sampleRate)
    {
        var bytes = new byte[samples.Count * 2];
        for (int i = 0; i < samples.Count; i++)
        {
            bytes[2 * i] = (byte)samples[i];
            bytes[2 * i + 1] = (byte)(samples[i] >> 8);
        }
        using var stream = new MemoryStream(bytes.Length + 64);
        using (var writer = new WaveFileWriter(new IgnoreDisposeStream(stream), new WaveFormat(sampleRate, 16, 1)))
            writer.Write(bytes, 0, bytes.Length);
        return stream.ToArray();
    }
}

/// <summary>Where the optional eSpeak NG component is installed, and whether it is.</summary>
public static class EspeakComponent
{
    public const string LibraryFileName = "libespeak-ng.dll";
    public const string DataFolderName = "espeak-ng-data";

    /// <summary>%LOCALAPPDATA%\Vox\components\espeak-ng: the library and its data folder.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vox", "components", "espeak-ng");

    public static bool IsInstalled(string directory) =>
        File.Exists(Path.Combine(directory, LibraryFileName)) && Directory.Exists(Path.Combine(directory, DataFolderName));
}
