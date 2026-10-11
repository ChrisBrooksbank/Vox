using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Configuration;
using Vox.Core.Enterprise;
using Vox.Core.Tests.TestSupport;
using Vox.Core.Updates;
using Xunit;

namespace Vox.Core.Tests.Enterprise;

/// <summary>Built-in defaults &lt; machine defaults &lt; the user's settings &lt; policy.</summary>
public sealed class MachineSettingsTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("VoxMachine_");

    public void Dispose() => _directory.Delete(recursive: true);

    private string PathOf(string name) => Path.Combine(_directory.FullName, name);

    private static FakeRegistry Installer(string name, object value) =>
        new FakeRegistry().Set(RegistryRoot.LocalMachine, MachineSettings.DefaultsKey, name, value);

    private SettingsManager Manager(JsonObject? machine) =>
        new(NullLogger<SettingsManager>.Instance, PathOf("default-settings.json"), PathOf("settings.json")) { MachineDefaults = machine };

    [Fact]
    public void NothingSet_IsNull()
    {
        Assert.Null(MachineSettings.Read(new FakeRegistry(), PathOf("machine-settings.json")));
        Assert.Null(MachineSettings.Read(new FakeRegistry(), null));
    }

    [Fact]
    public void TheInstallersValues_AreReadAsSettings_AndEmptyOnesSkipped()
    {
        var registry = Installer("UpdateChannel", "Off")
            .Set(RegistryRoot.LocalMachine, MachineSettings.DefaultsKey, "KeyboardLayout", "Laptop")
            .Set(RegistryRoot.LocalMachine, MachineSettings.DefaultsKey, "ModifierKey", "")
            .Set(RegistryRoot.LocalMachine, MachineSettings.DefaultsKey, "FirstRunCompleted", "1");

        var defaults = MachineSettings.Read(registry, null)!;

        Assert.Equal("Off", defaults["UpdateChannel"]!.GetValue<string>());
        Assert.Equal("Laptop", defaults["KeyboardLayout"]!.GetValue<string>());
        Assert.True(defaults["FirstRunCompleted"]!.GetValue<bool>());
        Assert.False(defaults.ContainsKey("ModifierKey"));
    }

    [Fact]
    public void TheFile_GivesAnySetting_AndTheInstallerWinsOverIt()
    {
        File.WriteAllText(PathOf("machine-settings.json"),
            """{ "SchemaVersion": 2, "speechRateWpm": 260, "UpdateChannel": "Beta", "SleepApps": ["game"], "Bogus": 1, "MaxLineLength": "long" }""");

        var defaults = MachineSettings.Read(Installer("UpdateChannel", "Stable"), PathOf("machine-settings.json"))!;

        Assert.Equal(260, defaults["SpeechRateWpm"]!.GetValue<int>());
        Assert.Equal("Stable", defaults["UpdateChannel"]!.GetValue<string>());
        Assert.Equal("game", defaults["SleepApps"]![0]!.GetValue<string>());
        Assert.False(defaults.ContainsKey("SchemaVersion"));
        Assert.False(defaults.ContainsKey("Bogus"));
        Assert.False(defaults.ContainsKey("MaxLineLength"));
    }

    [Fact]
    public void AnUnreadableFile_IsIgnored()
    {
        File.WriteAllText(PathOf("machine-settings.json"), "{ not json");

        Assert.Null(MachineSettings.Read(new FakeRegistry(), PathOf("machine-settings.json")));
    }

    [Fact]
    public void ANewUser_StartsWithTheMachineDefaults_OverTheBuiltInOnes()
    {
        File.WriteAllText(PathOf("default-settings.json"), """{ "SpeechRateWpm": 180, "SpeechPitch": 40 }""");
        var machine = MachineSettings.Read(Installer("SpeechRateWpm", 240).Set(RegistryRoot.LocalMachine, MachineSettings.DefaultsKey, "UpdateChannel", "Off"), null);

        var settings = Manager(machine).Load();

        Assert.Equal(240, settings.SpeechRateWpm);
        Assert.Equal(40, settings.SpeechPitch);
        Assert.Equal(UpdateChannel.Off, settings.UpdateChannel);
    }

    [Fact]
    public void TheUsersOwnSettings_WinOverTheMachineDefaults_WhichFillWhatTheFileLeavesOut()
    {
        File.WriteAllText(PathOf("settings.json"), """{ "SchemaVersion": 2, "SpeechRateWpm": 300 }""");
        var machine = MachineSettings.Read(Installer("SpeechRateWpm", 240).Set(RegistryRoot.LocalMachine, MachineSettings.DefaultsKey, "KeyboardLayout", "Laptop"), null);

        var settings = Manager(machine).Load();

        Assert.Equal(300, settings.SpeechRateWpm);
        Assert.Equal(KeyboardLayout.Laptop, settings.KeyboardLayout);
    }

    [Fact]
    public void WithoutMachineDefaults_LoadingIsUnchanged()
    {
        File.WriteAllText(PathOf("settings.json"), """{ "SchemaVersion": 2, "SpeechRateWpm": 300 }""");

        var settings = Manager(null).Load();

        Assert.Equal(300, settings.SpeechRateWpm);
        Assert.Equal(KeyboardLayout.Desktop, settings.KeyboardLayout);
    }

    [Fact]
    public void Overlay_MatchesNamesInAnyCase_AndChangesNeitherObject()
    {
        var under = new JsonObject { ["speechratewpm"] = 100, ["SpeechPitch"] = 30 };
        var over = new JsonObject { ["SpeechRateWpm"] = 200 };

        var result = SettingValues.Overlay(under, over);

        Assert.Equal(2, result.Count);
        Assert.Equal(200, result["SpeechRateWpm"]!.GetValue<int>());
        Assert.Equal(100, under["speechratewpm"]!.GetValue<int>());
        Assert.Single(over);
    }

    [Theory]
    [InlineData("SpeechRateWpm", "250", 250)]
    [InlineData("SpeechRateWpm", 250, 250)]
    [InlineData("AudioCuesEnabled", 0, false)]
    [InlineData("AudioCuesEnabled", "true", true)]
    [InlineData("KeyboardLayout", "laptop", KeyboardLayout.Laptop)]
    [InlineData("KeyboardLayout", 1, KeyboardLayout.Laptop)]
    [InlineData("KeyboardLayout", "1", KeyboardLayout.Laptop)]
    [InlineData("VoiceName", "Zira", "Zira")]
    public void TryConvert_ReadsRegistryValues(string setting, object raw, object expected)
    {
        Assert.True(SettingValues.TryConvert(SettingValues.Find(setting)!, raw, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("SpeechRateWpm", "fast")]
    [InlineData("AudioCuesEnabled", "2")]
    [InlineData("KeyboardLayout", "Tablet")]
    [InlineData("KeyboardLayout", 5)]
    [InlineData("LanguageVoices", "not json")]
    public void TryConvert_RejectsWhatDoesNotFit(string setting, object raw)
    {
        Assert.False(SettingValues.TryConvert(SettingValues.Find(setting)!, raw, out _));
    }

    [Fact]
    public void TryConvert_ReadsMapsAsJson()
    {
        Assert.True(SettingValues.TryConvert(SettingValues.Find("LanguageVoices")!, """{ "fr": "Hortense" }""", out var value));
        Assert.Equal("Hortense", ((Dictionary<string, string>)value!)["fr"]);
    }
}
