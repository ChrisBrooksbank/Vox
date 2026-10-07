using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;
using Vox.Core.Audio;
using Xunit;

namespace Vox.Core.Tests.Audio;

public class AudioOutputDevicesTests
{
    private static readonly string[] Devices = ["Speakers (Realtek Audio)", "Headphones (USB Audio)"];

    [Theory]
    [InlineData("Headphones (USB Audio)", "Headphones (USB Audio)")]
    [InlineData("headphones (usb audio)", "Headphones (USB Audio)")]
    [InlineData("Unplugged device", null)]
    [InlineData(null, null)]
    public void Choose_ByName(string? name, string? expected)
    {
        Assert.Equal(expected, AudioOutputDevices.Choose(Devices, name, d => d));
    }

    private sealed class FakeOutput : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void ChangingTheOutputDevice_ReopensTheOutput()
    {
        var opened = new List<FakeOutput>();
        using var player = new AudioCuePlayer(NullLogger<AudioCuePlayer>.Instance, Path.GetTempPath(), _ =>
        {
            var output = new FakeOutput();
            opened.Add(output);
            return output;
        });
        var silence = new SilenceProvider(AudioCuePlayer.MixFormat).ToSampleProvider();

        player.PlayProvider(silence);
        player.OutputDevice = "Headphones (USB Audio)";
        player.PlayProvider(silence);

        Assert.Equal(2, opened.Count);
        Assert.True(opened[0].Disposed);
        Assert.False(opened[1].Disposed);
    }

    [Fact]
    public void KeepAwake_OpensTheOutputAtOnce_AndIdleNeverClosesIt()
    {
        var opened = new List<FakeOutput>();
        using var player = new AudioCuePlayer(NullLogger<AudioCuePlayer>.Instance, Path.GetTempPath(), _ =>
        {
            var output = new FakeOutput();
            opened.Add(output);
            return output;
        }) { IdleClose = TimeSpan.Zero };

        player.KeepAwake = true;
        Assert.True(player.IsOutputOpen);

        player.CloseIfIdle();
        Assert.True(player.IsOutputOpen);

        player.KeepAwake = false;
        player.CloseIfIdle();
        Assert.False(player.IsOutputOpen);
        Assert.Single(opened);
    }

    [Fact]
    public void AStreamStillPlaying_KeepsTheOutputOpen()
    {
        using var player = new AudioCuePlayer(NullLogger<AudioCuePlayer>.Instance, Path.GetTempPath(), _ => new FakeOutput())
        {
            IdleClose = TimeSpan.Zero,
        };

        // An utterance longer than the idle time, which the (fake) device hasn't played yet
        player.PlayStream(new SilenceProvider(AudioCuePlayer.MixFormat).ToSampleProvider().Take(TimeSpan.FromSeconds(30)));
        player.CloseIfIdle();

        Assert.True(player.IsOutputOpen);
    }
}
