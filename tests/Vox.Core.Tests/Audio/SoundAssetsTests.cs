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
