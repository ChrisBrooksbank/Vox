using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Configuration;
using Xunit;

namespace Vox.Core.Tests.Configuration;

public sealed class SettingsMigrationsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VoxMigrations_" + Guid.NewGuid().ToString("N"));

    public SettingsMigrationsTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    private string UserPath => Path.Combine(_directory, "settings.json");

    private SettingsManager Manager(bool readOnly = false) =>
        new(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "missing-defaults.json"), UserPath) { ReadOnly = readOnly };

    [Fact]
    public void FileWithoutAVersion_IsVersionOne()
    {
        Assert.Equal(1, SettingsMigrations.VersionOf(JsonNode.Parse("""{ "SpeechRateWpm": 300 }""")!.AsObject()));
        Assert.Equal(2, SettingsMigrations.VersionOf(JsonNode.Parse("""{ "schemaversion": 2 }""")!.AsObject()));
    }

    [Fact]
    public void VersionOne_SleepAppsLoseTheirExeSuffix()
    {
        var settings = JsonNode.Parse("""{ "sleepApps": ["notepad.exe", "Code", "GAME.EXE"] }""")!.AsObject();

        Assert.Equal(1, SettingsMigrations.Migrate(settings));

        Assert.Equal("""["notepad","Code","GAME"]""", settings["sleepApps"]!.ToJsonString());
        Assert.Equal(VoxSettings.CurrentSchemaVersion, SettingsMigrations.VersionOf(settings));
    }

    [Fact]
    public void CurrentAndNewerFiles_AreLeftAlone()
    {
        var current = JsonNode.Parse("""{ "SchemaVersion": 2, "SleepApps": ["x.exe"] }""")!.AsObject();
        var newer = JsonNode.Parse("""{ "SchemaVersion": 99, "SleepApps": ["x.exe"] }""")!.AsObject();

        Assert.Equal(2, SettingsMigrations.Migrate(current));
        Assert.Equal(99, SettingsMigrations.Migrate(newer));

        Assert.Equal("""["x.exe"]""", current["SleepApps"]!.ToJsonString());
        Assert.Equal("""["x.exe"]""", newer["SleepApps"]!.ToJsonString());
    }

    [Fact]
    public void Loading_AnOldFile_MigratesAndSavesIt_KeepingACopy()
    {
        var old = """{ "SpeechRateWpm": 320, "SleepApps": ["notepad.exe"], "FirstRunCompleted": true }""";
        File.WriteAllText(UserPath, old);

        var settings = Manager().Load();

        Assert.Equal(["notepad"], settings.SleepApps);
        Assert.Equal(320, settings.SpeechRateWpm);
        Assert.Equal(old, File.ReadAllText(Path.Combine(_directory, "settings.v1.json")));
        var saved = JsonNode.Parse(File.ReadAllText(UserPath))!.AsObject();
        Assert.Equal(VoxSettings.CurrentSchemaVersion, SettingsMigrations.VersionOf(saved));
    }

    [Fact]
    public void ReadOnly_MigratesInMemoryOnly()
    {
        var old = """{ "SleepApps": ["notepad.exe"] }""";
        File.WriteAllText(UserPath, old);

        Assert.Equal(["notepad"], Manager(readOnly: true).Load().SleepApps);
        Assert.Equal(old, File.ReadAllText(UserPath));
    }

    [Fact]
    public void FileFromANewerVox_IsNeverOverwritten()
    {
        var newer = """{ "SchemaVersion": 99, "SpeechRateWpm": 280, "SomethingNew": true }""";
        File.WriteAllText(UserPath, newer);
        var manager = Manager();

        var settings = manager.Load();
        manager.Save(settings with { SpeechRateWpm = 300 });

        Assert.Equal(280, settings.SpeechRateWpm);
        Assert.Equal(newer, File.ReadAllText(UserPath));
    }

    [Fact]
    public void Save_WritesTheCurrentVersion()
    {
        Manager().Save(new VoxSettings { SchemaVersion = 1 });

        Assert.Equal(VoxSettings.CurrentSchemaVersion,
            SettingsMigrations.VersionOf(JsonNode.Parse(File.ReadAllText(UserPath))!.AsObject()));
    }
}
