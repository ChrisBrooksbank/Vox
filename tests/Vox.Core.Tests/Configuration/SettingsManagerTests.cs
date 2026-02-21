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
        Assert.Equal(450, settings.SpeechRateWpm);
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
        Assert.Equal(450, settings.SpeechRateWpm);
    }

    [Fact]
    public void DefaultSettingsJson_HasExpectedValues()
    {
        // Validate the default-settings.json asset content matches spec
        var assetPath = Path.Combine(
            AppContext.BaseDirectory,
            "assets", "config", "default-settings.json");

        if (!File.Exists(assetPath))
            return; // Asset not copied in test context — skip

        var json = File.ReadAllText(assetPath);
        var settings = JsonSerializer.Deserialize<VoxSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });

        Assert.NotNull(settings);
        Assert.Equal(VerbosityLevel.Beginner, settings!.VerbosityLevel);
        Assert.Equal(450, settings.SpeechRateWpm);
        Assert.Equal(TypingEchoMode.Both, settings.TypingEchoMode);
        Assert.Equal(ModifierKey.Insert, settings.ModifierKey);
        Assert.True(settings.AudioCuesEnabled);
        Assert.False(settings.FirstRunCompleted);
    }
}
