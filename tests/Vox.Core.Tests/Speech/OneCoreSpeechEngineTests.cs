using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;
using Vox.Core.Audio;
using Vox.Core.Speech;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class OneCoreSpeechEngineTests
{
    private sealed class Synthesizer : IOneCoreSynthesizer
    {
        public List<(string Text, OneCoreOptions Options)> Calls { get; } = new();
        public TaskCompletionSource? Gate { get; set; }

        public IReadOnlyList<OneCoreVoice> Voices { get; } =
            [new("Microsoft Aria", "en-US"), new("Microsoft Libby", "en-GB"), new("Microsoft Ryan", "en-GB")];

        public string? DefaultVoice => "Microsoft Aria";

        public async Task<byte[]> SynthesizeAsync(string text, OneCoreOptions options, CancellationToken cancellationToken)
        {
            Calls.Add((text, options));
            if (Gate is { } gate)
                await gate.Task.WaitAsync(cancellationToken);
            return Wav(TimeSpan.FromMilliseconds(50));
        }

        public static byte[] Wav(TimeSpan length)
        {
            var format = new WaveFormat(22050, 16, 1);
            using var stream = new MemoryStream();
            using (var writer = new WaveFileWriter(stream, format))
            {
                var silence = new byte[(int)(format.AverageBytesPerSecond * length.TotalSeconds)];
                writer.Write(silence, 0, silence.Length);
            }
            return stream.ToArray();
        }
    }

    /// <summary>Records streams; <see cref="PlayAll"/> plays them as a device would.</summary>
    private sealed class Player : IAudioStreamPlayer
    {
        private readonly List<ISampleProvider> _streams = new();
        public int Started { get { lock (_streams) return _streams.Count; } }

        public void PlayStream(ISampleProvider source)
        {
            lock (_streams) _streams.Add(source);
        }

        /// <summary>Reads each stream until it ends; returns how many samples each gave.</summary>
        public List<int> PlayAll()
        {
            List<ISampleProvider> streams;
            lock (_streams) streams = _streams.ToList();
            var totals = new List<int>();
            var buffer = new float[1024];
            foreach (var stream in streams)
            {
                int total = 0, read;
                do { read = stream.Read(buffer, 0, buffer.Length); total += read; } while (read == buffer.Length);
                totals.Add(total);
            }
            return totals;
        }

        public async Task WaitForStreams(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (Started < count)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException();
                await Task.Delay(5);
            }
        }
    }

    private readonly Synthesizer _synthesizer = new();
    private readonly Player _player = new();
    private readonly OneCoreSpeechEngine _engine;

    public OneCoreSpeechEngineTests()
    {
        _engine = new OneCoreSpeechEngine(_synthesizer, _player, NullLogger<OneCoreSpeechEngine>.Instance);
    }

    [Fact]
    public async Task Speak_CompletesWhenTheAudioHasPlayed()
    {
        var speaking = _engine.SpeakAsync(new Utterance("Hello", SpeechPriority.Normal));
        await _player.WaitForStreams(1);
        Assert.False(speaking.IsCompleted);
        Assert.True(_engine.IsSpeaking);

        Assert.True(_player.PlayAll()[0] > 0);

        await speaking.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(_engine.IsSpeaking);
    }

    [Fact]
    public async Task Cancel_StopsTheAudioAtOnce()
    {
        var speaking = _engine.SpeakAsync(new Utterance("Hello", SpeechPriority.Normal));
        await _player.WaitForStreams(1);

        _engine.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => speaking.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, _player.PlayAll()[0]);
    }

    [Fact]
    public async Task CancellationToken_StopsTheAudio()
    {
        using var cts = new CancellationTokenSource();
        var speaking = _engine.SpeakAsync(new Utterance("Hello", SpeechPriority.Normal), cts.Token);
        await _player.WaitForStreams(1);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => speaking.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Interrupt_StopsWhatIsPlaying()
    {
        var first = _engine.SpeakAsync(new Utterance("First", SpeechPriority.Normal));
        await _player.WaitForStreams(1);

        var second = _engine.SpeakAsync(new Utterance("Second", SpeechPriority.Interrupt));
        await _player.WaitForStreams(2);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(2)));
        var played = _player.PlayAll();
        Assert.Equal(0, played[0]);
        Assert.True(played[1] > 0);
        await second.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CancelledWhileSynthesizing_IsNeverPlayed()
    {
        _synthesizer.Gate = new TaskCompletionSource();
        var speaking = _engine.SpeakAsync(new Utterance("Hello", SpeechPriority.Normal));

        _engine.Cancel();
        _synthesizer.Gate.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => speaking.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, _player.Started);
    }

    [Fact]
    public async Task BlankText_IsNotSynthesized()
    {
        await _engine.SpeakAsync(new Utterance("  ", SpeechPriority.Normal));

        Assert.Empty(_synthesizer.Calls);
    }

    [Fact]
    public async Task RatePitchVolumeAndVoice_AreSentToTheSynthesizer()
    {
        _engine.SetRate(360);
        _engine.SetPitch(75);
        _engine.SetVolume(40);
        _engine.SetVoice("Microsoft Libby");

        var speaking = _engine.SpeakAsync(new Utterance("Hello", SpeechPriority.Normal));
        await _player.WaitForStreams(1);
        _player.PlayAll();
        await speaking;

        var options = _synthesizer.Calls.Single().Options;
        Assert.Equal(2.0, options.SpeakingRate, 3);
        Assert.Equal(1.5, options.Pitch, 3);
        Assert.Equal(0.4, options.Volume, 3);
        Assert.Equal("Microsoft Libby", options.Voice);
    }

    [Theory]
    [InlineData(180, 1.0)]
    [InlineData(450, 2.5)]
    [InlineData(50, 0.5)]
    [InlineData(900, 5.0)]
    [InlineData(5000, 5.0)]
    public void WpmToSpeakingRate_IsProportional_WithinTheEnginesRange(int wpm, double expected)
    {
        Assert.Equal(expected, OneCoreSpeechEngine.WpmToSpeakingRate(wpm), 3);
    }

    [Fact]
    public void MaxRate_IsTheFullRangeVoxOffers()
    {
        Assert.Equal(ISpeechEngine.MaxSupportedWpm, _engine.MaxRateWpm);
    }

    [Fact]
    public void Voices_LanguagesAndCurrentVoice()
    {
        Assert.Equal(["Microsoft Aria", "Microsoft Libby", "Microsoft Ryan"], _engine.GetAvailableVoices());
        Assert.Equal(["en-US", "en-GB"], _engine.GetLanguages());
        Assert.Equal("Microsoft Aria", _engine.CurrentVoice);

        _engine.SetVoice("Microsoft Ryan");
        Assert.Equal("Microsoft Ryan", _engine.CurrentVoice);
        _engine.SetVoice("Not installed");
        Assert.Equal("Microsoft Ryan", _engine.CurrentVoice);
        _engine.SetVoice("");
        Assert.Equal("Microsoft Aria", _engine.CurrentVoice);
    }
}
