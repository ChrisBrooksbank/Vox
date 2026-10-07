using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;
using Vox.Core.Audio;
using Vox.Core.Speech;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class EspeakSynthesizerTests
{
    private sealed class FakeEspeak : IEspeakApi
    {
        public int SampleRate => 22050;
        public List<string> Calls { get; } = new();
        public int BlocksBeforeCancel { get; set; } = int.MaxValue;
        public CancellationTokenSource? CancelDuringSynthesis { get; set; }

        public IReadOnlyList<SynthesizerVoice> ListVoices() =>
            [new("Afrikaans", "af"), new("English (Great Britain)", "en-gb"), new("French (France)", "fr-fr")];

        public void SetVoice(string name) => Calls.Add($"voice {name}");
        public void SetRate(int wpm) => Calls.Add($"rate {wpm}");
        public void SetPitch(int pitch) => Calls.Add($"pitch {pitch}");
        public void SetVolume(int volume) => Calls.Add($"volume {volume}");

        public void Synthesize(string text, Func<short[], bool> onSamples)
        {
            Calls.Add($"say {text}");
            for (int block = 0; block < 4; block++)
            {
                if (block == BlocksBeforeCancel)
                    CancelDuringSynthesis?.Cancel();
                if (!onSamples([1000, -1000, 500]))
                {
                    Calls.Add("aborted");
                    return;
                }
            }
        }
    }

    private readonly FakeEspeak _api = new();
    private readonly EspeakSynthesizer _synthesizer;

    public EspeakSynthesizerTests()
    {
        _synthesizer = new EspeakSynthesizer(_api);
    }

    [Fact]
    public async Task Synthesize_ProducesAWaveFileOfTheSamples()
    {
        var wav = await _synthesizer.SynthesizeAsync("hello", new SynthesisOptions(null, 1.0, 1.0, 1.0), CancellationToken.None);

        using var reader = new WaveFileReader(new MemoryStream(wav));
        Assert.Equal(new WaveFormat(22050, 16, 1), reader.WaveFormat);
        Assert.Equal(12, reader.SampleCount);
        var first = new byte[2];
        reader.ReadExactly(first);
        Assert.Equal(1000, BitConverter.ToInt16(first));
    }

    [Fact]
    public async Task Options_AreSetBeforeSpeaking()
    {
        await _synthesizer.SynthesizeAsync("bonjour", new SynthesisOptions("French (France)", 2.0, 1.5, 0.5), CancellationToken.None);

        Assert.Equal(["voice French (France)", "rate 360", "pitch 75", "volume 50", "say bonjour"], _api.Calls);
    }

    [Fact]
    public async Task TheDefaultVoice_IsEnglish_AndIsOnlySetWhenItChanges()
    {
        await _synthesizer.SynthesizeAsync("one", new SynthesisOptions(null, 1.0, 1.0, 1.0), CancellationToken.None);
        await _synthesizer.SynthesizeAsync("two", new SynthesisOptions(null, 1.0, 1.0, 1.0), CancellationToken.None);

        Assert.Equal("English (Great Britain)", _synthesizer.DefaultVoice);
        Assert.Single(_api.Calls, c => c.StartsWith("voice"));
    }

    [Fact]
    public async Task Rate_IsKeptWithinWhatESpeakSpeaks()
    {
        await _synthesizer.SynthesizeAsync("fast", new SynthesisOptions(null, 5.0, 1.0, 1.0), CancellationToken.None);

        Assert.Contains("rate 450", _api.Calls);
        Assert.Equal(EspeakSynthesizer.MaxWpm, _synthesizer.MaxRateWpm);
    }

    [Fact]
    public async Task Cancelling_StopsSynthesis()
    {
        using var cts = new CancellationTokenSource();
        _api.CancelDuringSynthesis = cts;
        _api.BlocksBeforeCancel = 1;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _synthesizer.SynthesizeAsync("long text", new SynthesisOptions(null, 1.0, 1.0, 1.0), cts.Token));
        Assert.Contains("aborted", _api.Calls);
    }

    [Fact]
    public void TheEngine_IsLimitedToESpeaksRate()
    {
        var engine = new WaveSpeechEngine(_synthesizer, new NullPlayer(), NullLogger<WaveSpeechEngine>.Instance);

        Assert.Equal(EspeakSynthesizer.MaxWpm, engine.MaxRateWpm);
        Assert.Equal(["Afrikaans", "English (Great Britain)", "French (France)"], engine.GetAvailableVoices());
    }

    [Fact]
    public void Component_IsInstalledOnlyWithLibraryAndData()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VoxEspeak_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            Assert.False(EspeakComponent.IsInstalled(directory));
            File.WriteAllText(Path.Combine(directory, EspeakComponent.LibraryFileName), "");
            Assert.False(EspeakComponent.IsInstalled(directory));
            Directory.CreateDirectory(Path.Combine(directory, EspeakComponent.DataFolderName));
            Assert.True(EspeakComponent.IsInstalled(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class NullPlayer : IAudioStreamPlayer
    {
        public void PlayStream(ISampleProvider source) { }
    }
}
