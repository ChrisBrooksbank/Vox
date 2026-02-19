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
}
