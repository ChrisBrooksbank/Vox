using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Configuration;
using Vox.Core.Navigation;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Configuration;

public class ProfileSettingsTests
{
    [Fact]
    public void Merge_LaterLayersWin_AndOnlyWhatTheySet()
    {
        var normal = new VoxSettings { SpeechRateWpm = 200, PunctuationLevel = PunctuationLevel.Some };

        var merged = ProfileSettings.Merge(normal,
        [
            new JsonObject { ["SpeechRateWpm"] = 300, ["PunctuationLevel"] = "All" },
            new JsonObject { ["SpeechRateWpm"] = 250 },
        ]);

        Assert.Equal(250, merged.SpeechRateWpm);
        Assert.Equal(PunctuationLevel.All, merged.PunctuationLevel);
        Assert.Equal(normal.VerbosityLevel, merged.VerbosityLevel);
    }

    [Fact]
    public void Merge_SkipsUnknownAndBadValues_KeepingTheRest()
    {
        var merged = ProfileSettings.Merge(new VoxSettings(),
        [
            new JsonObject { ["NoSuchSetting"] = 1, ["PunctuationLevel"] = "Loud", ["SpeechRateWpm"] = 320, ["SchemaVersion"] = 99 },
        ]);

        Assert.Equal(320, merged.SpeechRateWpm);
        Assert.Equal(PunctuationLevel.Some, merged.PunctuationLevel);
        Assert.Equal(VoxSettings.CurrentSchemaVersion, merged.SchemaVersion);
    }

    [Fact]
    public void Diff_HasOnlyWhatChanged()
    {
        var before = new VoxSettings();
        var after = before with { SpeechRateWpm = 333, SleepApps = ["notepad"] };

        var diff = ProfileSettings.Diff(before, after);

        Assert.Equal(["SleepApps", "SpeechRateWpm"], diff.Select(p => p.Key).Order());
        Assert.Equal(333, diff["SpeechRateWpm"]!.GetValue<int>());
        Assert.Empty(ProfileSettings.Diff(before, before with { SleepApps = [] }));
    }
}

public class ProfileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VoxProfiles_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        var store = new ProfileStore(_directory);
        var profile = new ConfigurationProfile("Excel: fast", new JsonObject { ["SpeechRateWpm"] = 300 }, ["EXCEL"], SayAll: true);

        Assert.True(store.Save(profile));
        var loaded = Assert.Single(store.LoadAll());

        Assert.Equal("Excel: fast", loaded.Name);
        Assert.Equal(["EXCEL"], loaded.Apps);
        Assert.True(loaded.SayAll);
        Assert.Equal(300, loaded.Settings["SpeechRateWpm"]!.GetValue<int>());
        Assert.True(File.Exists(Path.Combine(_directory, "Excel_ fast.json")));
    }

    [Fact]
    public void BadFile_IsLeftOut_AndLogged()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "broken.json"), "{ not json");
        var logger = new CapturingLogger<ProfileStore>();
        var store = new ProfileStore(_directory, logger);
        store.Save(new ConfigurationProfile("Good", new JsonObject(), []));

        Assert.Equal(["Good"], store.LoadAll().Select(p => p.Name));
        Assert.NotEmpty(logger.Entries);
    }

    [Fact]
    public void NoFolder_NoProfiles_NothingSaved()
    {
        var store = new ProfileStore(null);

        Assert.False(store.IsAvailable);
        Assert.Empty(store.LoadAll());
        Assert.False(store.Save(new ConfigurationProfile("x", new JsonObject(), [])));
    }

    [Fact]
    public void Delete_RemovesTheFile()
    {
        var store = new ProfileStore(_directory);
        store.Save(new ConfigurationProfile("Gone", new JsonObject(), []));

        store.Delete("Gone");

        Assert.Empty(store.LoadAll());
    }
}

public class ProfileManagerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VoxProfiles_" + Guid.NewGuid().ToString("N"));
    private readonly ProfileStore _store;

    public ProfileManagerTests()
    {
        _store = new ProfileStore(_directory);
        _store.Save(new ConfigurationProfile("Excel", new JsonObject { ["SpeechRateWpm"] = 300, ["VerbosityLevel"] = "Advanced" }, ["excel"]));
        _store.Save(new ConfigurationProfile("Reading", new JsonObject { ["SpeechRateWpm"] = 150 }, [], SayAll: true));
        _store.Save(new ConfigurationProfile("Quiet", new JsonObject { ["SpeechRateWpm"] = 400, ["PunctuationLevel"] = "None" }, []));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static readonly VoxSettings Normal = new() { SpeechRateWpm = 200 };

    [Fact]
    public void MergeOrder_NormalAppTriggerManual()
    {
        var profiles = new ProfileManager(_store);
        Assert.Equal(200, profiles.Apply(Normal).SpeechRateWpm);

        profiles.SetForegroundApp("EXCEL");
        Assert.Equal(300, profiles.Apply(Normal).SpeechRateWpm);
        Assert.Equal(VerbosityLevel.Advanced, profiles.Apply(Normal).VerbosityLevel);

        profiles.SetSayAll(true);
        Assert.Equal(150, profiles.Apply(Normal).SpeechRateWpm);
        // What the trigger doesn't set still comes from the application's profile
        Assert.Equal(VerbosityLevel.Advanced, profiles.Apply(Normal).VerbosityLevel);

        profiles.ActivateManually("quiet");
        Assert.Equal(400, profiles.Apply(Normal).SpeechRateWpm);
        Assert.Equal(["Excel", "Reading", "Quiet"], profiles.Active.Select(p => p.Name));

        profiles.ActivateManually(null);
        profiles.SetSayAll(false);
        profiles.SetForegroundApp("notepad");
        Assert.Same(Normal, profiles.Apply(Normal));
    }

    [Fact]
    public void Switched_IsRaisedForApplicationAndManualChanges_NotSayAll()
    {
        var profiles = new ProfileManager(_store);
        var switches = new List<string>();
        profiles.Switched += (_, name) => switches.Add(ProfileCommands.SwitchAnnouncement(name));

        profiles.SetForegroundApp("excel");
        profiles.SetForegroundApp("excel");
        profiles.SetSayAll(true);
        profiles.SetSayAll(false);
        profiles.ActivateManually("Quiet");
        profiles.SetForegroundApp("notepad");
        profiles.ActivateManually(null);

        Assert.Equal(["Profile Excel", "Profile Quiet", "Normal configuration"], switches);
    }

    [Fact]
    public void ActivateManually_UnknownProfile_IsRefused() =>
        Assert.False(new ProfileManager(_store).ActivateManually("nope"));

    [Fact]
    public void RecordChanges_GoToTheTopProfile_ExceptGlobalOnes()
    {
        var profiles = new ProfileManager(_store);
        profiles.SetForegroundApp("excel");

        var normal = profiles.RecordChanges(new JsonObject { ["SpeechRateWpm"] = 320, ["SleepApps"] = new JsonArray("x") });

        Assert.Equal(["SleepApps"], normal.Select(p => p.Key));
        Assert.Equal(320, profiles.Apply(Normal).SpeechRateWpm);
        // Saved
        Assert.Equal(320, new ProfileManager(_store).Profiles.Single(p => p.Name == "Excel").Settings["SpeechRateWpm"]!.GetValue<int>());
    }

    [Fact]
    public void RecordChanges_NoProfileActive_AllForTheNormalConfiguration()
    {
        var changes = new JsonObject { ["SpeechRateWpm"] = 320 };

        Assert.Equal(320, new ProfileManager(_store).RecordChanges(changes)["SpeechRateWpm"]!.GetValue<int>());
    }

    [Fact]
    public void CreateRenameDelete()
    {
        var profiles = new ProfileManager(_store);

        Assert.True(profiles.Create("Teams", ["ms-teams"]));
        Assert.False(profiles.Create("teams"));
        Assert.False(profiles.Create("  "));
        Assert.True(profiles.Rename("Teams", "Meetings"));
        Assert.False(profiles.Rename("Meetings", "Excel"));
        profiles.ActivateManually("Meetings");
        profiles.Delete("Meetings");

        Assert.Null(profiles.ManualProfile);
        Assert.Equal(["Excel", "Quiet", "Reading"], new ProfileManager(_store).Profiles.Select(p => p.Name).Order());
    }

    [Fact]
    public void Triggers_AppActivationAndOnlyOneSayAllProfile()
    {
        var profiles = new ProfileManager(_store);

        profiles.SetAppActivation("Quiet", "notepad", true);
        profiles.SetSayAllTrigger("Quiet", true);
        profiles.SetForegroundApp("Notepad");

        Assert.Equal(400, profiles.Apply(Normal).SpeechRateWpm);
        Assert.False(profiles.Profiles.Single(p => p.Name == "Reading").SayAll);

        profiles.SetAppActivation("Quiet", "notepad", false);
        Assert.Equal(200, profiles.Apply(Normal).SpeechRateWpm);
    }
}

public class SettingsMonitorProfileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VoxProfileSettings_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void EditsWhileAProfileIsActive_GoToIt_AndTheNormalConfigurationIsUntouched()
    {
        var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "defaults.json"), Path.Combine(_directory, "settings.json"));
        using var monitor = new SettingsMonitor(manager, NullLogger<SettingsMonitor>.Instance);
        var store = new ProfileStore(Path.Combine(_directory, "profiles"));
        store.Save(new ConfigurationProfile("Excel", new JsonObject { ["SpeechRateWpm"] = 300 }, ["EXCEL"]));
        var profiles = new ProfileManager(store);
        monitor.AttachProfiles(profiles);
        var heard = new List<int>();
        monitor.OnChange((s, _) => heard.Add(s.SpeechRateWpm));

        profiles.SetForegroundApp("EXCEL");
        Assert.Equal(300, monitor.CurrentValue.SpeechRateWpm);

        monitor.UpdateSettings(monitor.CurrentValue with { SpeechRateWpm = 350, StartAtLogon = true });
        Assert.Equal(350, monitor.CurrentValue.SpeechRateWpm);
        Assert.Equal(200, monitor.NormalValue.SpeechRateWpm);
        // A global setting goes to the normal configuration even so
        Assert.True(monitor.NormalValue.StartAtLogon);

        profiles.SetForegroundApp("notepad");
        Assert.Equal(200, monitor.CurrentValue.SpeechRateWpm);
        Assert.Equal([300, 350, 200], heard);

        monitor.UpdateSettings(monitor.CurrentValue with { SpeechRateWpm = 220 });
        Assert.Equal(220, manager.Load().SpeechRateWpm);
    }
}

public class SayAllReadingChangedTests
{
    private sealed class OneLine : ISayAllSource
    {
        public string? CurrentLanguage => null;
        public Task<string?> CurrentLineAsync(CancellationToken token) => Task.FromResult<string?>("Hello");
        public Task<string?> NextLineAsync(CancellationToken token) => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task StartAndEnd_AreReported()
    {
        using var queue = new SpeechQueue(new RecordingSpeechEngine(), NullLogger<SpeechQueue>.Instance);
        var controller = new SayAllController(queue, NullLogger<SayAllController>.Instance);
        var states = new List<bool>();
        var ended = new TaskCompletionSource();
        controller.ReadingChanged += (_, reading) =>
        {
            lock (states) states.Add(reading);
            if (!reading)
                ended.TrySetResult();
        };

        controller.Start(new OneLine());
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([true, false], states);
    }
}
