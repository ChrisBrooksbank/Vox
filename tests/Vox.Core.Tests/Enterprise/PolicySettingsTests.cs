using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Configuration;
using Vox.Core.Enterprise;
using Vox.Core.Lifecycle;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Vox.Core.Updates;
using Xunit;

namespace Vox.Core.Tests.Enterprise;

public class PolicySettingsTests
{
    private static FakeRegistry MachineSetting(string name, object value) =>
        new FakeRegistry().Set(RegistryRoot.LocalMachine, PolicySettings.SettingsKey, name, value);

    [Fact]
    public void NothingSet_IsEmpty_AndChangesNothing()
    {
        var policy = PolicySettings.Read(new FakeRegistry());
        var settings = new VoxSettings();

        Assert.True(policy.IsEmpty);
        Assert.Empty(policy.LockedSettings);
        Assert.Same(settings, policy.Apply(settings));
        Assert.Equal(RunPolicy.Normal, policy.Restrict(RunPolicy.Normal));
        Assert.Equal("No Vox policies set", policy.Describe());
    }

    [Fact]
    public void DisableNetwork_TurnsTheNetworkAndUpdateChecksOff()
    {
        var policy = PolicySettings.Read(new FakeRegistry().Machine(PolicySettings.DisableNetworkValue, 1));
        var run = policy.Restrict(RunPolicy.Normal);

        Assert.False(run.AllowNetwork);
        Assert.True(run.AllowAddOns && run.AllowAi && run.AllowSettingsWrites);
        Assert.False(run.IsSecure);
        Assert.Equal(UpdateChannel.Off, policy.Apply(new VoxSettings { UpdateChannel = UpdateChannel.Beta }).UpdateChannel);
        Assert.True(policy.IsLocked(nameof(VoxSettings.UpdateChannel)));
    }

    [Fact]
    public void AddOnsAndAi_AreTurnedOffSeparately()
    {
        var run = PolicySettings.Read(new FakeRegistry().Machine(PolicySettings.DisableAddOnsValue, 1).User(PolicySettings.DisableAiValue, "1"))
            .Restrict(RunPolicy.Normal);

        Assert.False(run.AllowAddOns);
        Assert.False(run.AllowAi);
        Assert.True(run.AllowNetwork);
    }

    [Fact]
    public void Restrict_NeverLoosensTheSecureScreens()
    {
        var run = PolicySettings.Read(new FakeRegistry().Machine(PolicySettings.DisableNetworkValue, 0)).Restrict(RunPolicy.Secure);

        Assert.True(run.IsSecure);
        Assert.False(run.AllowNetwork);
        Assert.False(run.AllowAddOns);
    }

    [Fact]
    public void TheComputersPolicy_WinsOverTheUsers()
    {
        var registry = new FakeRegistry()
            .User(PolicySettings.UpdateChannelValue, "Beta")
            .Machine(PolicySettings.UpdateChannelValue, "Stable");

        Assert.Equal(UpdateChannel.Stable, PolicySettings.Read(registry).EnforcedSettings[nameof(VoxSettings.UpdateChannel)]);
    }

    [Fact]
    public void TheUsersPolicy_AppliesWhereTheComputerSaysNothing_ExceptOnSecureScreens()
    {
        var registry = new FakeRegistry().User(PolicySettings.UpdateChannelValue, "beta");

        Assert.Equal(UpdateChannel.Beta, PolicySettings.Read(registry).EnforcedSettings[nameof(VoxSettings.UpdateChannel)]);
        Assert.True(PolicySettings.Read(registry, includeUser: false).IsEmpty);
    }

    [Fact]
    public void DisableUpdates_WinsOverAChannel()
    {
        var policy = PolicySettings.Read(new FakeRegistry()
            .Machine(PolicySettings.UpdateChannelValue, "Beta")
            .Machine(PolicySettings.DisableUpdatesValue, 1));

        Assert.Equal(UpdateChannel.Off, policy.Apply(new VoxSettings()).UpdateChannel);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void StartAtLogon_ForcesTheSettingEitherWay(int value, bool expected)
    {
        var policy = PolicySettings.Read(new FakeRegistry().Machine(PolicySettings.StartAtLogonValue, value));

        Assert.Equal(expected, policy.Apply(new VoxSettings { StartAtLogon = !expected }).StartAtLogon);
        Assert.True(policy.IsLocked(nameof(VoxSettings.StartAtLogon)));
        Assert.False(policy.IsLocked(nameof(VoxSettings.SpeechRateWpm)));
    }

    [Fact]
    public void AnySetting_CanBeEnforcedByName()
    {
        var registry = MachineSetting("SpeechRateWpm", 250)
            .Set(RegistryRoot.LocalMachine, PolicySettings.SettingsKey, "keyboardlayout", "Laptop")
            .Set(RegistryRoot.LocalMachine, PolicySettings.SettingsKey, "AudioCuesEnabled", "0")
            .Set(RegistryRoot.LocalMachine, PolicySettings.SettingsKey, "SleepApps", new[] { "game", " vlc " });
        var policy = PolicySettings.Read(registry);

        var applied = policy.Apply(new VoxSettings());

        Assert.Equal(250, applied.SpeechRateWpm);
        Assert.Equal(KeyboardLayout.Laptop, applied.KeyboardLayout);
        Assert.False(applied.AudioCuesEnabled);
        Assert.Equal(["game", "vlc"], applied.SleepApps);
        Assert.Equal(
            new[] { "AudioCuesEnabled", "KeyboardLayout", "SleepApps", "SpeechRateWpm" },
            policy.LockedSettings.Order());
        Assert.Empty(policy.Problems);
    }

    [Fact]
    public void Apply_GivesBackTheSameSettings_WhenTheyAlreadyFollowThePolicy()
    {
        var policy = PolicySettings.Read(MachineSetting("SpeechRateWpm", 250));
        var settings = new VoxSettings { SpeechRateWpm = 250 };

        Assert.Same(settings, policy.Apply(settings));
    }

    [Fact]
    public void BadValues_AreReportedAndIgnored()
    {
        var registry = MachineSetting("SpeechRateWpm", "fast")
            .Set(RegistryRoot.LocalMachine, PolicySettings.SettingsKey, "NoSuchSetting", 1)
            .Set(RegistryRoot.LocalMachine, PolicySettings.SettingsKey, "KeyboardLayout", 9)
            .Machine(PolicySettings.UpdateChannelValue, "Nightly")
            .Machine(PolicySettings.DisableNetworkValue, "maybe")
            .Machine(PolicySettings.SettingsLocationValue, "Cloud")
            .Machine("Unknown", 1);

        var policy = PolicySettings.Read(registry);

        Assert.True(policy.IsEmpty);
        Assert.Equal(7, policy.Problems.Count);
        Assert.Contains(policy.Problems, p => p.Contains("NoSuchSetting"));
        Assert.Contains(policy.Problems, p => p.Contains("Unknown is not a Vox policy"));
    }

    [Fact]
    public void LockSettings_LocksEverySettingButBookkeeping()
    {
        var policy = PolicySettings.Read(new FakeRegistry().Machine(PolicySettings.LockSettingsValue, 1));

        Assert.True(policy.IsLocked(nameof(VoxSettings.SpeechRateWpm)));
        Assert.True(policy.IsLocked(nameof(VoxSettings.BrailleTable)));
        Assert.False(policy.IsLocked(nameof(VoxSettings.FirstRunCompleted)));
        Assert.False(policy.IsLocked(nameof(VoxSettings.SchemaVersion)));
        Assert.False(policy.IsLocked("NoSuchSetting"));
    }

    [Fact]
    public void Restrain_KeepsLockedSettings_AndLetsTheRestChange()
    {
        var policy = PolicySettings.Read(new FakeRegistry()
            .Machine(PolicySettings.LockSettingsValue, 1)
            .Set(RegistryRoot.LocalMachine, PolicySettings.SettingsKey, "SpeechVolume", 80));
        var current = new VoxSettings { SpeechRateWpm = 200, SpeechVolume = 80 };

        var result = policy.Restrain(current, current with { SpeechRateWpm = 300, SpeechVolume = 50, FirstRunCompleted = true });

        Assert.Equal(200, result.SpeechRateWpm);
        Assert.Equal(80, result.SpeechVolume);
        Assert.True(result.FirstRunCompleted);
    }

    [Theory]
    [InlineData("Local", SettingsLocation.Local)]
    [InlineData("roaming", SettingsLocation.Roaming)]
    public void SettingsLocation_IsRead(string value, SettingsLocation expected)
    {
        Assert.Equal(expected, PolicySettings.Read(new FakeRegistry().Machine(PolicySettings.SettingsLocationValue, value)).SettingsLocation);
    }

    [Fact]
    public void Describe_NamesWhatIsSet()
    {
        var policy = PolicySettings.Read(new FakeRegistry().Machine(PolicySettings.DisableNetworkValue, 1).Machine(PolicySettings.LockSettingsValue, 1));

        Assert.Equal("Vox policies: network off; all settings locked; enforced: UpdateChannel=Off", policy.Describe());
    }

    [Fact]
    public void SettingsMonitor_FollowsThePolicy_WhateverChangesTheSettings()
    {
        var directory = Directory.CreateTempSubdirectory("VoxPolicy_");
        try
        {
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance,
                Path.Combine(directory.FullName, "defaults.json"), Path.Combine(directory.FullName, "settings.json"));
            manager.Save(new VoxSettings { SpeechRateWpm = 300, UpdateChannel = UpdateChannel.Beta });
            var policy = PolicySettings.Read(MachineSetting("SpeechRateWpm", 220).Machine(PolicySettings.DisableUpdatesValue, 1));
            using var monitor = new SettingsMonitor(manager, NullLogger<SettingsMonitor>.Instance, policy);

            Assert.Equal(220, monitor.CurrentValue.SpeechRateWpm);
            Assert.Equal(UpdateChannel.Off, monitor.CurrentValue.UpdateChannel);

            VoxSettings? notified = null;
            monitor.OnChange((s, _) => notified = s);
            monitor.UpdateSettings(monitor.CurrentValue with { SpeechRateWpm = 400, SpeechPitch = 60 });

            Assert.Equal(220, monitor.CurrentValue.SpeechRateWpm);
            Assert.Equal(60, monitor.CurrentValue.SpeechPitch);
            Assert.Equal(220, notified!.SpeechRateWpm);
            Assert.Same(policy, monitor.Policy);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static readonly SpeechOptions NoSpeechOptions = new([], [], 400, SpeechCapabilities.None);

    [Fact]
    public void SettingsPages_LockTheFieldsThePolicySets()
    {
        var policy = PolicySettings.Read(MachineSetting("SpeechRateWpm", 220).Machine(PolicySettings.DisableUpdatesValue, 1));
        var settings = policy.Apply(new VoxSettings());

        var pages = SettingsPages.WithPolicy([SettingsPages.General(), SettingsPages.Speech(NoSpeechOptions)], policy, settings);

        var locked = pages.SelectMany(p => p.Fields).Where(f => f.Locked).Select(f => f.Label).ToList();
        Assert.Equal(["Check for &updates (set by your organization)", "&Rate (words per minute) (set by your organization)"], locked);
        var rate = (NumberField)pages[1].Fields.Single(f => f.Locked);
        Assert.Same(settings, rate.Set(settings, 300));
        Assert.Equal("220", SettingsPages.ValueText(rate, settings));
        var updates = (ChoiceField)pages[0].Fields.Single(f => f.Locked);
        Assert.Equal("Never", SettingsPages.ValueText(updates, settings));
    }

    [Fact]
    public void SettingsPages_WithoutLocks_AreLeftAlone()
    {
        IReadOnlyList<SettingsPage> pages = [SettingsPages.General()];

        Assert.Same(pages, SettingsPages.WithPolicy(pages, PolicySettings.None, new VoxSettings()));
    }

    [Fact]
    public void SettingsPages_LockEverything_WithLockSettings()
    {
        var policy = PolicySettings.Read(new FakeRegistry().Machine(PolicySettings.LockSettingsValue, 1));

        var pages = SettingsPages.WithPolicy(
            [SettingsPages.General(), SettingsPages.Speech(NoSpeechOptions), SettingsPages.Braille(), SettingsPages.Mouse()],
            policy, new VoxSettings());

        Assert.All(pages.SelectMany(p => p.Fields), f => Assert.True(f.Locked, f.Label));
    }

    [Fact]
    public void ChangesOf_FindsEverySettingAFieldChanges()
    {
        var synthesizer = SettingsPages.Speech(new SpeechOptions(
            [new SpeechEngineDescriptor("A", "A", () => throw new NotSupportedException())], [], 400, SpeechCapabilities.None)).Fields[0];

        var changes = SettingsPages.ChangesOf(synthesizer, new VoxSettings { VoiceName = "Zira" });

        Assert.Equal(["SpeechEngine", "VoiceName"], changes.Order());
    }

    [Fact]
    public void ValueText_ReadsToggles()
    {
        var field = SettingsPages.General().Fields.OfType<ToggleField>().First();

        Assert.Equal("Off", SettingsPages.ValueText(field, new VoxSettings()));
        Assert.Equal("On", SettingsPages.ValueText(field, new VoxSettings { StartAtLogon = true }));
    }
}
