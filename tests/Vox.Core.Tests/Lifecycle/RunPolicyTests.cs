using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Configuration;
using Vox.Core.Lifecycle;
using Xunit;

namespace Vox.Core.Tests.Lifecycle;

public class RunPolicyTests
{
    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--secure" }, true)]
    [InlineData(new[] { "/SECURE" }, true)]
    [InlineData(new[] { "--verbose" }, false)]
    public void FromArgs(string[] args, bool secure)
    {
        Assert.Equal(secure, RunPolicy.FromArgs(args).IsSecure);
    }

    [Fact]
    public void Normal_AllowsEverything()
    {
        var policy = RunPolicy.Normal;

        Assert.True(policy.AllowSettingsWrites && policy.AllowAddOns && policy.AllowNetwork && policy.AllowAi
            && policy.AllowUserProfileAccess && policy.AllowSetupWizard);
        Assert.Equal(SettingsManager.DefaultUserSettingsPath, policy.SettingsPath);
    }

    [Fact]
    public void Secure_AllowsNothingThatTouchesUserDataOrTheNetwork()
    {
        var policy = RunPolicy.Secure;

        Assert.False(policy.AllowSettingsWrites);
        Assert.False(policy.AllowAddOns);
        Assert.False(policy.AllowNetwork);
        Assert.False(policy.AllowAi);
        Assert.False(policy.AllowUserProfileAccess);
        Assert.False(policy.AllowSetupWizard);
        Assert.Equal(RunPolicy.SecureSettingsPath, policy.SettingsPath);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), policy.LogDirectory);
    }

    [Fact]
    public void ReadOnlySettings_AreNotWritten()
    {
        var path = Path.Combine(Path.GetTempPath(), "VoxReadOnly_" + Guid.NewGuid().ToString("N") + ".json");
        var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, "missing-defaults.json", path) { ReadOnly = true };

        manager.Save(new VoxSettings { SpeechRateWpm = 333 });

        Assert.False(File.Exists(path));
    }
}
