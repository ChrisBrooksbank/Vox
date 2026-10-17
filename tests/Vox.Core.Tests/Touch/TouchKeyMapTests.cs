using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Touch;
using Xunit;

namespace Vox.Core.Tests.Touch;

public class TouchKeyMapTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string UserFile(string json)
    {
        var path = Path.Combine(_dir, "touch-keymap.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void BuiltIn_BindsTheDefaultGestures()
    {
        var map = TouchKeyMap.LoadBuiltIn();
        Assert.Equal(NavigationCommand.StopSpeech, map.Resolve(TouchGestureKind.Tap, 2, false, InteractionMode.Focus));
        Assert.Equal(NavigationCommand.TouchActivate, map.Resolve(TouchGestureKind.DoubleTap, 1, false, InteractionMode.Focus));
        Assert.Equal(NavigationCommand.TouchNextItem, map.Resolve(TouchGestureKind.FlickRight, 1, true, InteractionMode.Browse));
    }

    [Fact]
    public void BrowseBindings_ApplyOnlyInABrowsingDocument()
    {
        var map = TouchKeyMap.LoadBuiltIn();
        Assert.Equal(NavigationCommand.NextLine, map.Resolve(TouchGestureKind.FlickDown, 1, true, InteractionMode.Browse));
        Assert.Null(map.Resolve(TouchGestureKind.FlickDown, 1, true, InteractionMode.Focus));
        Assert.Null(map.Resolve(TouchGestureKind.FlickDown, 1, false, InteractionMode.Browse));
    }

    [Fact]
    public void UserFile_OverridesAndUnbinds()
    {
        var path = UserFile("""
            { "bindings": [
              { "gesture": "Tap", "fingers": 2, "mode": "Any", "command": "None" },
              { "gesture": "FlickUp", "fingers": 2, "mode": "Any", "command": "ReadCurrentLine" } ] }
            """);
        var map = TouchKeyMap.Load(path, out var warnings);
        Assert.Empty(warnings);
        Assert.Null(map.Resolve(TouchGestureKind.Tap, 2, false, InteractionMode.Focus));
        Assert.Equal(NavigationCommand.ReadCurrentLine, map.Resolve(TouchGestureKind.FlickUp, 2, false, InteractionMode.Focus));
    }

    [Fact]
    public void BadEntriesAreReported_AndBrokenFileLeavesTheDefaults()
    {
        var map = TouchKeyMap.Load(UserFile("""{ "bindings": [ { "gesture": "Wave", "fingers": 1, "command": "StopSpeech" }, { "gesture": "Tap", "fingers": 2, "command": "Nope" } ] }"""), out var warnings);
        Assert.Equal(2, warnings.Count);
        Assert.Equal(NavigationCommand.StopSpeech, map.Resolve(TouchGestureKind.Tap, 2, false, InteractionMode.Focus));

        map = TouchKeyMap.Load(UserFile("{ not json"), out warnings);
        Assert.Single(warnings);
        Assert.Equal(NavigationCommand.StopSpeech, map.Resolve(TouchGestureKind.Tap, 2, false, InteractionMode.Focus));
    }

    [Fact]
    public void EveryBuiltInBinding_HasACatalogEntry()
    {
        foreach (var binding in TouchKeyMap.LoadBuiltIn().Bindings)
            Assert.True(CommandCatalog.Has(binding.Command), binding.Command.ToString());
    }
}
