using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Configuration;
using Vox.Core.Lifecycle;
using Xunit;

namespace Vox.Core.Tests.Lifecycle;

public class SecureSettingsPipeTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "VoxPipe_" + Guid.NewGuid().ToString("N"), "secure-settings.json");

    [Fact]
    public void Receiver_WritesValidSettings_AndMarksFirstRunDone()
    {
        var target = TempFile();
        try
        {
            var receiver = new SecureSettingsReceiver(NullLogger<SecureSettingsReceiver>.Instance, target);

            Assert.True(receiver.Accept(SettingsManager.Serialize(new VoxSettings { SpeechRateWpm = 310 })));

            Assert.True(SettingsManager.TryParse(File.ReadAllText(target), out var saved));
            Assert.Equal(310, saved!.SpeechRateWpm);
            Assert.True(saved.FirstRunCompleted);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(target)!, recursive: true);
        }
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    public void Receiver_RefusesInvalidSettings_AndWritesNothing(string json)
    {
        var target = TempFile();
        var receiver = new SecureSettingsReceiver(NullLogger<SecureSettingsReceiver>.Instance, target);

        Assert.False(receiver.Accept(json));
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void Receiver_RefusesOversizedInput()
    {
        var target = TempFile();
        var receiver = new SecureSettingsReceiver(NullLogger<SecureSettingsReceiver>.Instance, target);

        Assert.False(receiver.Accept(new string(' ', SecureSettingsPipe.MaxBytes + 1)));
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task Send_ReachesTheServingReceiver()
    {
        var target = TempFile();
        var pipe = "VoxTest_" + Guid.NewGuid().ToString("N");
        using var cts = new CancellationTokenSource();
        var receiver = new SecureSettingsReceiver(NullLogger<SecureSettingsReceiver>.Instance, target);
        var serve = SecureSettingsPipe.ServeAsync(receiver, cts.Token, pipe);
        try
        {
            var sent = await Task.Run(() => SecureSettingsPipe.Send(new VoxSettings { SpeechRateWpm = 290 }, 5000, pipe));

            Assert.True(sent);
            Assert.True(SettingsManager.TryParse(File.ReadAllText(target), out var saved));
            Assert.Equal(290, saved!.SpeechRateWpm);
        }
        finally
        {
            cts.Cancel();
            await serve;
            if (Directory.Exists(Path.GetDirectoryName(target)!)) Directory.Delete(Path.GetDirectoryName(target)!, recursive: true);
        }
    }

    [Fact]
    public void Send_WithNoService_ReturnsFalse() =>
        Assert.False(SecureSettingsPipe.Send(new VoxSettings(), 200, "VoxTest_none_" + Guid.NewGuid().ToString("N")));
}
