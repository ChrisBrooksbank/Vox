using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Configuration;
using Vox.Core.Lifecycle;
using Xunit;

namespace Vox.Core.Tests.Lifecycle;

public class SecureScreenSettingsTests
{
    private static SettingsManager Manager(string userPath) =>
        new(NullLogger<SettingsManager>.Instance, "missing-defaults.json", userPath);

    [Fact]
    public void Copy_WritesTheSettingsWhereTheSecureInstanceReadsThem()
    {
        var dir = Path.Combine(Path.GetTempPath(), "VoxSecure_" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(dir, "secure-settings.json");
        try
        {
            var message = SecureScreenSettings.Copy(Manager(Path.Combine(dir, "user.json")),
                new VoxSettings { SpeechRateWpm = 320 }, RunPolicy.Normal, target);

            Assert.Equal("Settings copied to the sign-in screens", message);
            var copied = Manager(target).Load();
            Assert.Equal(320, copied.SpeechRateWpm);
            Assert.True(copied.FirstRunCompleted);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Copy_InSecureMode_IsRefused()
    {
        var target = Path.Combine(Path.GetTempPath(), "VoxSecure_" + Guid.NewGuid().ToString("N") + ".json");

        var message = SecureScreenSettings.Copy(Manager(target + ".user"), new VoxSettings(), RunPolicy.Secure, target);

        Assert.Equal("Not available on this screen", message);
        Assert.False(File.Exists(target));
    }
}
