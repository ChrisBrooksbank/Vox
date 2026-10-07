using NAudio.Wave;
using Vox.Core.Audio;
using Xunit;

namespace Vox.Core.Tests.Audio;

public class SoundAssetsTests
{
    private static readonly string SoundsDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "sounds");

    [Fact]
    public void TheDefaultScheme_HasAValidWavFileForEveryCue()
    {
        var files = AudioCuePlayer.SchemeFiles(SoundsDir, AudioCuePlayer.DefaultScheme);

        foreach (var cue in AudioCuePlayer.Cues)
        {
            Assert.True(files.TryGetValue(cue, out var path), $"The default scheme has no sound for {cue}");
            Assert.True(File.Exists(path), $"Missing sound file {path}");
            using var reader = new WaveFileReader(path);
            Assert.True(reader.TotalTime > TimeSpan.Zero && reader.TotalTime < TimeSpan.FromSeconds(1), $"{cue} is too long");
            Assert.True(reader.WaveFormat.Channels is 1 or 2);
        }
    }

    [Fact]
    public void EveryScheme_HasAManifestNamingFilesItHas()
    {
        foreach (var folder in Directory.GetDirectories(SoundsDir))
        {
            var scheme = Path.GetFileName(folder);
            Assert.True(File.Exists(Path.Combine(folder, "manifest.json")), $"Scheme {scheme} has no manifest");
            foreach (var (cue, path) in AudioCuePlayer.SchemeFiles(SoundsDir, scheme))
                Assert.True(File.Exists(path), $"Scheme {scheme}: {cue} names a missing file");
        }
    }

    [Fact]
    public void AnotherScheme_OverridesSomeCues_AndFallsBackForTheRest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VoxSounds_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "default"));
            Directory.CreateDirectory(Path.Combine(directory, "soft"));
            File.WriteAllText(Path.Combine(directory, "default", "manifest.json"),
                """{ "cues": { "boundary": "boundary.wav", "wrap": "wrap.wav" } }""");
            File.WriteAllText(Path.Combine(directory, "soft", "manifest.json"),
                """{ "cues": { "boundary": "soft-boundary.wav", "wrap": "..\\..\\outside.wav" } }""");

            var files = AudioCuePlayer.SchemeFiles(directory, "soft");

            Assert.Equal(Path.Combine(directory, "soft", "soft-boundary.wav"), files["boundary"]);
            // A path out of the scheme's folder is ignored
            Assert.Equal(Path.Combine(directory, "default", "wrap.wav"), files["wrap"]);
            // An unknown scheme is the default one
            Assert.Equal(Path.Combine(directory, "default", "boundary.wav"), AudioCuePlayer.SchemeFiles(directory, "missing")["boundary"]);
            Assert.Empty(AudioCuePlayer.SchemeFiles(directory, "..").Where(f => !f.Value.StartsWith(Path.Combine(directory, "default"))));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
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

    [Fact]
    public void IdleClose_AlreadyFiredBeforeANewCue_DoesNotCloseTheNewCue()
    {
        // The idle timer fired just before a cue was added and runs after it: the device the
        // new cue plays on must stay open
        var (player, opened) = CreatePlayer();
        using var _ = player;
        player.IdleClose = TimeSpan.FromSeconds(5);
        player.PlayProvider(Cue());

        player.CloseIfIdle();

        Assert.True(player.IsOutputOpen);
        Assert.False(opened[0].Disposed);
    }
}
