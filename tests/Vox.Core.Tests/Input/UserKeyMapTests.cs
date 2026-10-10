using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Xunit;

namespace Vox.Core.Tests.Input;

public sealed class UserKeyMapTests : IDisposable
{
    private static readonly string ConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "config");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VoxKeyMap_" + Guid.NewGuid().ToString("N"));

    public UserKeyMapTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    private KeyMap Load(string userJson, out IReadOnlyList<string> warnings, KeyboardLayout layout = KeyboardLayout.Desktop)
    {
        var path = Path.Combine(_directory, KeyMap.UserFileName);
        File.WriteAllText(path, userJson);
        var map = KeyMap.LoadLayout(ConfigDirectory, layout, path, out var error, out warnings);
        Assert.Null(error);
        return map;
    }

    [Fact]
    public void UserBinding_ReplacesTheDefaultOnTheSameKey()
    {
        // J: next heading not yet visited, by default; the user wants it as next heading
        var map = Load("""{ "bindings": [ { "modifiers": "None", "vkCode": 74, "mode": "Browse", "command": "NextHeading" } ] }""", out _);

        Assert.True(map.TryResolve(KeyModifiers.None, 74, InteractionMode.Browse, out var command));
        Assert.Equal(NavigationCommand.NextHeading, command);
        // Everything else is still there
        Assert.True(map.TryResolve(KeyModifiers.None, 72, InteractionMode.Browse, out command));
        Assert.Equal(NavigationCommand.NextHeading, command);
    }

    [Fact]
    public void UserBinding_AddsNewKeys()
    {
        var map = Load("""{ "bindings": [ { "modifiers": "Insert|Shift", "vkCode": 72, "mode": "Any", "command": "SayTime" } ] }""", out var warnings);

        Assert.True(map.TryResolveOutsideDocument(KeyModifiers.Insert | KeyModifiers.Shift, 72, out var command, out _));
        Assert.Equal(NavigationCommand.SayTime, command);
        Assert.Empty(warnings);
    }

    [Fact]
    public void None_UnbindsAKey_AndReportsACommandLeftWithoutOne()
    {
        var map = Load("""{ "bindings": [ { "modifiers": "None", "vkCode": 74, "mode": "Browse", "command": "None" } ] }""", out var warnings);

        Assert.False(map.TryResolve(KeyModifiers.None, 74, InteractionMode.Browse, out _));
        Assert.Contains(warnings, w => w.Contains("NextUnvisitedHeading") && w.Contains("without a key"));
    }

    [Fact]
    public void TwoBindingsOnOneKey_AreAConflict_AndTheLastWins()
    {
        var map = Load("""
            { "bindings": [
              { "modifiers": "Insert", "vkCode": 74, "mode": "Any", "command": "SayTime" },
              { "modifiers": "Insert", "vkCode": 74, "mode": "Any", "command": "SayBattery" }
            ] }
            """, out var warnings);

        Assert.True(map.TryResolve(KeyModifiers.Insert, 74, InteractionMode.Focus, out var command));
        Assert.Equal(NavigationCommand.SayBattery, command);
        Assert.Contains(warnings, w => w.Contains("both SayTime and SayBattery"));
    }

    [Fact]
    public void UnreadableUserFile_IsReported_AndTheStandardKeysStay()
    {
        var map = Load("{ not json", out var warnings);

        Assert.True(map.TryResolve(KeyModifiers.None, 72, InteractionMode.Browse, out var command));
        Assert.Equal(NavigationCommand.NextHeading, command);
        Assert.Contains(warnings, w => w.Contains("could not be read"));
    }

    [Fact]
    public void NoUserFile_IsTheLayoutAlone()
    {
        var withNone = KeyMap.LoadLayout(ConfigDirectory, KeyboardLayout.Laptop, Path.Combine(_directory, "missing.json"), out var error, out var warnings);
        var layout = KeyMap.LoadLayout(ConfigDirectory, KeyboardLayout.Laptop, out _, out _);

        Assert.Null(error);
        Assert.Empty(warnings);
        Assert.Equal(layout.Count, withNone.Count);
    }
}
