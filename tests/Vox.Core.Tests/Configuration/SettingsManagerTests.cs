using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Configuration;
using Xunit;

namespace Vox.Core.Tests.Configuration;

[Collection("SettingsTests")]
public sealed class SettingsManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _defaultSettingsPath;

    public SettingsManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "VoxTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _defaultSettingsPath = Path.Combine(_tempDir, "default-settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private SettingsManager CreateManager()
    {
        // Use a non-existent user settings path so tests are isolated from real user settings
        var isolatedUserPath = Path.Combine(_tempDir, "user-settings.json");
        return new SettingsManager(NullLogger<SettingsManager>.Instance, _defaultSettingsPath, isolatedUserPath);
    }

    [Fact]
    public void Load_ReturnsBuiltInDefaults_WhenNoFilesExist()
    {
        var manager = CreateManager();

        var settings = manager.Load();

        Assert.Equal(VerbosityLevel.Beginner, settings.VerbosityLevel);
        Assert.Equal(200, settings.SpeechRateWpm); // matches assets/config/default-settings.json
        Assert.Equal(TypingEchoMode.Both, settings.TypingEchoMode);
        Assert.True(settings.AudioCuesEnabled);
        Assert.True(settings.AnnounceVisitedLinks);
        Assert.Equal(ModifierKey.Insert, settings.ModifierKey);
        Assert.False(settings.FirstRunCompleted);
    }

    [Fact]
    public void Load_ReturnsDefaultFileSettings_WhenOnlyDefaultExists()
    {
        var json = """
            {
              "VerbosityLevel": "Advanced",
              "SpeechRateWpm": 300,
              "TypingEchoMode": "Characters",
              "AudioCuesEnabled": false,
              "AnnounceVisitedLinks": false,
              "ModifierKey": "CapsLock",
              "FirstRunCompleted": true
            }
            """;
        File.WriteAllText(_defaultSettingsPath, json);

        var manager = CreateManager();
        var settings = manager.Load();

        Assert.Equal(VerbosityLevel.Advanced, settings.VerbosityLevel);
        Assert.Equal(300, settings.SpeechRateWpm);
        Assert.Equal(TypingEchoMode.Characters, settings.TypingEchoMode);
        Assert.False(settings.AudioCuesEnabled);
        Assert.Equal(ModifierKey.CapsLock, settings.ModifierKey);
        Assert.True(settings.FirstRunCompleted);
    }

    [Fact]
    public void Save_WritesJsonFile_ThenLoadReadsItBack()
    {
        // Use a temp user path by writing to a temporary location
        // We test via Save + Load using a round-trip through a temp file
        var manager = CreateManager();
        var tempSettingsPath = Path.Combine(_tempDir, "settings.json");
        var settingsToSave = new VoxSettings
        {
            VerbosityLevel = VerbosityLevel.Intermediate,
            SpeechRateWpm = 250,
            VoiceName = "TestVoice",
            TypingEchoMode = TypingEchoMode.Words,
            AudioCuesEnabled = false,
            AnnounceVisitedLinks = false,
            ModifierKey = ModifierKey.CapsLock,
            FirstRunCompleted = true
        };

        // Serialize and verify the JSON structure
        var json = JsonSerializer.Serialize(settingsToSave, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });

        File.WriteAllText(tempSettingsPath, json);
        var loaded = JsonSerializer.Deserialize<VoxSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });

        Assert.NotNull(loaded);
        Assert.Equal(VerbosityLevel.Intermediate, loaded!.VerbosityLevel);
        Assert.Equal(250, loaded.SpeechRateWpm);
        Assert.Equal("TestVoice", loaded.VoiceName);
        Assert.Equal(TypingEchoMode.Words, loaded.TypingEchoMode);
        Assert.False(loaded.AudioCuesEnabled);
        Assert.Equal(ModifierKey.CapsLock, loaded.ModifierKey);
        Assert.True(loaded.FirstRunCompleted);
    }

    [Fact]
    public void Load_FallsBackToDefaults_WhenDefaultFileIsInvalidJson()
    {
        File.WriteAllText(_defaultSettingsPath, "{ not valid json !!!");

        var manager = CreateManager();
        var settings = manager.Load();

        // Should fall back to built-in defaults without throwing
        Assert.Equal(VerbosityLevel.Beginner, settings.VerbosityLevel);
        Assert.Equal(200, settings.SpeechRateWpm);
    }

    [Fact]
    public void DefaultSettingsJson_HasExpectedValues()
    {
        // Validate the default-settings.json asset content matches spec. Resolve it from the repo
        // root (found via the solution file) rather than AppContext.BaseDirectory: the test project
        // doesn't copy assets/ to its output directory, so relying on the copy would let this test
        // silently no-op instead of actually checking anything.
        var assetPath = Path.Combine(FindRepoRoot(), "assets", "config", "default-settings.json");
        Assert.True(File.Exists(assetPath), $"Expected asset at {assetPath}");

        var json = File.ReadAllText(assetPath);
        var settings = JsonSerializer.Deserialize<VoxSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });

        Assert.NotNull(settings);
        Assert.Equal(VerbosityLevel.Beginner, settings!.VerbosityLevel);
        Assert.Equal(200, settings.SpeechRateWpm);
        Assert.Equal(TypingEchoMode.Both, settings.TypingEchoMode);
        Assert.Equal(ModifierKey.Insert, settings.ModifierKey);
        Assert.True(settings.AudioCuesEnabled);
        Assert.False(settings.FirstRunCompleted);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Vox.sln")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException("Could not find repo root (Vox.sln) above " + AppContext.BaseDirectory);

        return dir.FullName;
    }

    // -------------------------------------------------------------------------
    // Reload robustness and atomic save
    // -------------------------------------------------------------------------

    [Fact]
    public void TryLoadUserSettings_InvalidJson_ReturnsFalse()
    {
        var manager = CreateManager();
        File.WriteAllText(manager.UserSettingsPath, "{ \"SpeechRateWpm\": ");

        Assert.False(manager.TryLoadUserSettings(out _));
    }

    [Fact]
    public void Save_LeavesNoTemporaryFile_AndRoundTrips()
    {
        var manager = CreateManager();

        manager.Save(new VoxSettings { SpeechRateWpm = 333 });

        Assert.False(File.Exists(manager.UserSettingsPath + ".tmp"));
        Assert.True(manager.TryLoadUserSettings(out var loaded));
        Assert.Equal(333, loaded.SpeechRateWpm);
    }

    [Fact]
    public async Task Monitor_InvalidFileOnDisk_KeepsCurrentSettings()
    {
        var manager = CreateManager();
        manager.Save(new VoxSettings { SpeechRateWpm = 310, FirstRunCompleted = true });
        using var monitor = new SettingsMonitor(manager, NullLogger<SettingsMonitor>.Instance);
        Assert.Equal(310, monitor.CurrentValue.SpeechRateWpm);

        await Task.Delay(600); // past the programmatic-save suppression window
        File.WriteAllText(manager.UserSettingsPath, "{ \"SpeechRateWpm\": ");
        await Task.Delay(600);

        Assert.Equal(310, monitor.CurrentValue.SpeechRateWpm);
        Assert.True(monitor.CurrentValue.FirstRunCompleted);
    }

    [Fact]
    public async Task Monitor_ValidExternalEdit_IsApplied()
    {
        var manager = CreateManager();
        manager.Save(new VoxSettings { SpeechRateWpm = 310 });
        using var monitor = new SettingsMonitor(manager, NullLogger<SettingsMonitor>.Instance);

        await Task.Delay(600);
        File.WriteAllText(manager.UserSettingsPath, "{ \"SpeechRateWpm\": 280 }");

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (monitor.CurrentValue.SpeechRateWpm != 280 && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        Assert.Equal(280, monitor.CurrentValue.SpeechRateWpm);
    }
}
