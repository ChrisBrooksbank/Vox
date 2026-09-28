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
