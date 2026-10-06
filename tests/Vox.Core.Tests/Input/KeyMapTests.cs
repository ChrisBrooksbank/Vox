using Vox.Core.Input;
using Vox.Core.Pipeline;
using Xunit;

namespace Vox.Core.Tests.Input;

public class KeyMapTests
{
    private static KeyMap BuildMap(string json) => KeyMap.LoadFromJson(json);

    private static KeyMap BuildMapWithBinding(
        string modifiers, int vkCode, string mode, string command)
    {
        var json = $$"""
        {
            "bindings": [
                { "modifiers": "{{modifiers}}", "vkCode": {{vkCode}}, "mode": "{{mode}}", "command": "{{command}}" }
            ]
        }
        """;
        return BuildMap(json);
    }

    [Fact]
    public void LoadFromJson_EmptyBindings_ReturnsEmptyMap()
    {
        var map = BuildMap("""{ "bindings": [] }""");
        Assert.Equal(0, map.Count);
    }

    [Fact]
    public void TryResolve_ExactBrowseModeMatch_ReturnsCommand()
    {
        var map = BuildMapWithBinding("None", 72, "Browse", "NextHeading");

        var resolved = map.TryResolve(KeyModifiers.None, 72, InteractionMode.Browse, out var command);

        Assert.True(resolved);
        Assert.Equal(NavigationCommand.NextHeading, command);
    }

    [Fact]
    public void TryResolve_BrowseModeBinding_DoesNotMatchFocusMode()
    {
        var map = BuildMapWithBinding("None", 72, "Browse", "NextHeading");

        var resolved = map.TryResolve(KeyModifiers.None, 72, InteractionMode.Focus, out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolve_AnyModeBinding_MatchesBrowseMode()
    {
        var map = BuildMapWithBinding("Insert", 32, "Any", "ToggleMode");

        var resolved = map.TryResolve(KeyModifiers.Insert, 32, InteractionMode.Browse, out var command);

        Assert.True(resolved);
        Assert.Equal(NavigationCommand.ToggleMode, command);
    }

    [Fact]
    public void TryResolve_AnyModeBinding_MatchesFocusMode()
    {
        var map = BuildMapWithBinding("Insert", 32, "Any", "ToggleMode");

        var resolved = map.TryResolve(KeyModifiers.Insert, 32, InteractionMode.Focus, out var command);

        Assert.True(resolved);
        Assert.Equal(NavigationCommand.ToggleMode, command);
    }

    [Fact]
    public void TryResolve_InsertModifier_ResolvedCorrectly()
    {
        var map = BuildMapWithBinding("Insert", 118, "Any", "ElementsList");

        var resolved = map.TryResolve(KeyModifiers.Insert, 118, InteractionMode.Browse, out var command);

        Assert.True(resolved);
        Assert.Equal(NavigationCommand.ElementsList, command);
    }

    [Fact]
    public void TryResolve_CompoundModifier_ResolvedCorrectly()
    {
        var map = BuildMapWithBinding("Insert|Ctrl", 38, "Any", "ReadCurrentWord");

        var resolved = map.TryResolve(KeyModifiers.Insert | KeyModifiers.Ctrl, 38, InteractionMode.Browse, out var command);

        Assert.True(resolved);
        Assert.Equal(NavigationCommand.ReadCurrentWord, command);
    }

    [Fact]
    public void TryResolve_WrongModifiers_ReturnsFalse()
    {
        var map = BuildMapWithBinding("Insert", 32, "Any", "ToggleMode");

        var resolved = map.TryResolve(KeyModifiers.None, 32, InteractionMode.Browse, out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolve_WrongVkCode_ReturnsFalse()
    {
        var map = BuildMapWithBinding("Insert", 32, "Any", "ToggleMode");

        var resolved = map.TryResolve(KeyModifiers.Insert, 99, InteractionMode.Browse, out _);

        Assert.False(resolved);
    }

    [Fact]
    public void LoadFromJson_SkipsEntriesWithUnknownCommand()
    {
        var json = """
        {
            "bindings": [
                { "modifiers": "None", "vkCode": 72, "mode": "Browse", "command": "UnknownCommand" },
                { "modifiers": "None", "vkCode": 75, "mode": "Browse", "command": "NextLink" }
            ]
        }
        """;

        var map = BuildMap(json);

        // Only the valid entry should be in the map (Browse mode = 1 entry)
        Assert.Equal(1, map.Count);
        Assert.True(map.TryResolve(KeyModifiers.None, 75, InteractionMode.Browse, out var cmd));
        Assert.Equal(NavigationCommand.NextLink, cmd);
    }

    [Theory]
    [InlineData("None", "Browse", "99")]       // a number, not a command
    [InlineData("None", "Browse", "3")]        // a number that happens to be a valid value
    [InlineData("None", "7", "NextLink")]      // a numeric mode
    [InlineData("64", "Browse", "NextLink")]   // a numeric modifier
    public void LoadFromJson_NumericNames_AreSkippedWithAWarning(string modifiers, string mode, string command)
    {
        var json = $$"""
        { "bindings": [ { "modifiers": "{{modifiers}}", "vkCode": 72, "mode": "{{mode}}", "command": "{{command}}" } ] }
        """;

        var map = KeyMap.LoadFromJson(json, out var warnings);

        Assert.Equal(0, map.Count);
        Assert.Single(warnings);
    }

    [Fact]
    public void LoadFromJson_AnyModeAddsBindingForBothModes()
    {
        var json = """
        {
            "bindings": [
                { "modifiers": "Insert", "vkCode": 40, "mode": "Any", "command": "SayAll" }
            ]
        }
        """;

        var map = BuildMap(json);

        // "Any" mode expands to 2 entries
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void LoadFromFile_DefaultKeymap_LoadsSuccessfully()
    {
        // Find default-keymap.json relative to the test assembly
        var baseDir = AppContext.BaseDirectory;
        var keymapPath = Path.Combine(baseDir, "..", "..", "..", "..", "..", "assets", "config", "default-keymap.json");
        keymapPath = Path.GetFullPath(keymapPath);

        Assert.True(File.Exists(keymapPath), $"Keymap file not found at: {keymapPath}");

        var map = KeyMap.LoadFromFile(keymapPath);

        Assert.True(map.Count > 0, "Default keymap should have at least one binding.");
        // H key in browse mode should map to NextHeading
        Assert.True(map.TryResolve(KeyModifiers.None, 72, InteractionMode.Browse, out var cmd));
        Assert.Equal(NavigationCommand.NextHeading, cmd);
    }

    [Fact]
    public void TryResolve_ShiftModifier_PrevHeading()
    {
        var map = BuildMapWithBinding("Shift", 72, "Browse", "PrevHeading");

        var resolved = map.TryResolve(KeyModifiers.Shift, 72, InteractionMode.Browse, out var command);

        Assert.True(resolved);
        Assert.Equal(NavigationCommand.PrevHeading, command);
    }

    [Fact]
    public void TryResolve_ReportsPassThrough()
    {
        var map = KeyMap.LoadFromJson("""
            {
                "bindings": [
                    { "modifiers": "None", "vkCode": 162, "mode": "Any", "command": "StopSpeech", "passThrough": true },
                    { "modifiers": "None", "vkCode": 72, "mode": "Browse", "command": "NextHeading" }
                ]
            }
            """);

        Assert.True(map.TryResolve(KeyModifiers.None, 162, InteractionMode.Focus, out var stop, out var stopPassThrough));
        Assert.Equal(NavigationCommand.StopSpeech, stop);
        Assert.True(stopPassThrough);

        Assert.True(map.TryResolve(KeyModifiers.None, 72, InteractionMode.Browse, out _, out var headingPassThrough));
        Assert.False(headingPassThrough);
    }

    [Fact]
    public void DefaultKeymap_BindsStopSpeechToCtrl_AndShiftDigitsToPreviousHeadingLevel()
    {
        var keymapPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "config", "default-keymap.json");
        var map = KeyMap.LoadFromFile(keymapPath);

        Assert.True(map.TryResolve(KeyModifiers.None, 0xA2, InteractionMode.Browse, out var left, out var pass));
        Assert.Equal(NavigationCommand.StopSpeech, left);
        Assert.True(pass);
        Assert.True(map.TryResolve(KeyModifiers.None, 0xA3, InteractionMode.Focus, out var right));
        Assert.Equal(NavigationCommand.StopSpeech, right);

        Assert.True(map.TryResolve(KeyModifiers.Shift, 0x32, InteractionMode.Browse, out var prev2));
        Assert.Equal(NavigationCommand.PrevHeadingLevel2, prev2);
        Assert.True(map.TryResolve(KeyModifiers.None, 0x46, InteractionMode.Browse, out var form));
        Assert.Equal(NavigationCommand.NextFormField, form);
    }

    [Fact]
    public void LoadBuiltIn_MatchesDefaultKeymapFile()
    {
        var keymapPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "config", "default-keymap.json");

        Assert.Equal(KeyMap.LoadFromFile(keymapPath).Count, KeyMap.LoadBuiltIn().Count);
    }

    [Fact]
    public void LoadFromFileOrBuiltIn_MissingOrInvalidFile_FallsBackAndReportsError()
    {
        var missing = KeyMap.LoadFromFileOrBuiltIn(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), out var missingError);
        Assert.NotNull(missingError);
        Assert.True(missing.Count > 0);

        var invalidPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(invalidPath, "{ not json");
            var invalid = KeyMap.LoadFromFileOrBuiltIn(invalidPath, out var invalidError);
            Assert.NotNull(invalidError);
            Assert.True(invalid.Count > 0);
        }
        finally
        {
            File.Delete(invalidPath);
        }
    }

    [Fact]
    public void TryResolveOutsideDocument_OnlyMatchesAnyBindings()
    {
        var map = KeyMap.LoadFromJson("""
            {
                "bindings": [
                    { "modifiers": "Insert", "vkCode": 32, "mode": "Any", "command": "ToggleMode" },
                    { "modifiers": "None", "vkCode": 27, "mode": "Focus", "command": "ExitFocusMode" }
                ]
            }
            """);

        Assert.True(map.TryResolveOutsideDocument(KeyModifiers.Insert, 32, out var toggle, out _));
        Assert.Equal(NavigationCommand.ToggleMode, toggle);
        Assert.False(map.TryResolveOutsideDocument(KeyModifiers.None, 27, out _, out _));
    }
}

public class DefaultKeyMapReadingKeysTests
{
    private static readonly KeyMap Map = KeyMap.LoadBuiltIn();

    [Theory]
    [InlineData(KeyModifiers.Ctrl, 39, NavigationCommand.NextWord)]
    [InlineData(KeyModifiers.Ctrl, 37, NavigationCommand.PrevWord)]
    [InlineData(KeyModifiers.Ctrl, 40, NavigationCommand.NextParagraph)]
    [InlineData(KeyModifiers.Ctrl, 38, NavigationCommand.PrevParagraph)]
    [InlineData(KeyModifiers.None, 36, NavigationCommand.StartOfLine)]
    [InlineData(KeyModifiers.None, 35, NavigationCommand.EndOfLine)]
    [InlineData(KeyModifiers.Ctrl, 36, NavigationCommand.TopOfDocument)]
    [InlineData(KeyModifiers.Ctrl, 35, NavigationCommand.BottomOfDocument)]
    public void BrowseModeReadingKeys_FollowNvda(KeyModifiers modifiers, int vk, NavigationCommand expected)
    {
        Assert.True(Map.TryResolve(modifiers, vk, InteractionMode.Browse, out var command));
        Assert.Equal(expected, command);
    }

    [Theory]
    [InlineData(KeyModifiers.None, 36)]
    [InlineData(KeyModifiers.Ctrl, 39)]
    public void ReadingKeys_ReachTheControlInFocusMode(KeyModifiers modifiers, int vk) =>
        Assert.False(Map.TryResolve(modifiers, vk, InteractionMode.Focus, out _));
}
