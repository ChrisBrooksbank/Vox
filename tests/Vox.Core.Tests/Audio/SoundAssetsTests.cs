using NAudio.Wave;
using Vox.Core.Audio;
using Xunit;

namespace Vox.Core.Tests.Audio;

public class SoundAssetsTests
{
    [Fact]
    public void EveryPhase1Cue_HasAValidWavFile()
    {
        var soundsDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "sounds");

        foreach (var cue in AudioCuePlayer.Phase1Sounds)
        {
            var path = Path.Combine(soundsDir, cue + ".wav");
            Assert.True(File.Exists(path), $"Missing sound file {path}");
            using var reader = new WaveFileReader(path);
            Assert.True(reader.TotalTime > TimeSpan.Zero);
        }
    }
}

public class AudioCueMixFormatTests
{
    private sealed class SilentProvider(int sampleRate, int channels) : NAudio.Wave.ISampleProvider
    {
        public NAudio.Wave.WaveFormat WaveFormat { get; } = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        public int Read(float[] buffer, int offset, int count) => 0;
    }

    [Theory]
    [InlineData(44100, 1)]
    [InlineData(44100, 2)]
    [InlineData(48000, 1)]
    [InlineData(22050, 2)]
    public void AnyMonoOrStereoCue_IsConvertedToTheMixFormat(int sampleRate, int channels)
    {
        var converted = AudioCuePlayer.ToMixFormat(new SilentProvider(sampleRate, channels));

        Assert.Equal(AudioCuePlayer.MixFormat.SampleRate, converted.WaveFormat.SampleRate);
        Assert.Equal(AudioCuePlayer.MixFormat.Channels, converted.WaveFormat.Channels);
    }
}

public class AudioCueIdleTests
{
    private sealed class FakeOutput : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private static (AudioCuePlayer player, List<FakeOutput> opened) CreatePlayer()
    {
        var opened = new List<FakeOutput>();
        var player = new AudioCuePlayer(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AudioCuePlayer>.Instance,
            Path.GetTempPath(),
            _ => { var o = new FakeOutput(); lock (opened) opened.Add(o); return o; })
        {
            IdleClose = TimeSpan.FromMilliseconds(100),
        };
        return (player, opened);
    }

    private static NAudio.Wave.ISampleProvider Cue() =>
        new NAudio.Wave.SilenceProvider(AudioCuePlayer.MixFormat).ToSampleProvider().Take(TimeSpan.FromMilliseconds(10));

    [Fact]
    public async Task Output_ClosesAfterCuesStop_AndReopensForTheNextCue()
    {
        var (player, opened) = CreatePlayer();
        using var _ = player;

        player.PlayProvider(Cue());
        player.PlayProvider(Cue());
        Assert.True(player.IsOutputOpen);
        Assert.Single(opened); // quick cues share one device

        await Task.Delay(400);
        Assert.False(player.IsOutputOpen);
        Assert.True(opened[0].Disposed);

        player.PlayProvider(Cue());
        Assert.True(player.IsOutputOpen);
        Assert.Equal(2, opened.Count);
    }

    [Fact]
    public void DisablingCues_ClosesTheOutputAtOnce()
    {
        var (player, opened) = CreatePlayer();
        using var _ = player;
        player.PlayProvider(Cue());

        player.IsEnabled = false;

        Assert.False(player.IsOutputOpen);
        Assert.True(opened[0].Disposed);
    }
}
