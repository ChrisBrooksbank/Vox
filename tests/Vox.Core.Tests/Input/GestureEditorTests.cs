using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Xunit;

namespace Vox.Core.Tests.Input;

public sealed class GestureEditorTests : IDisposable
{
    private static readonly string ConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "config");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VoxGestures_" + Guid.NewGuid().ToString("N"));

    public GestureEditorTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    private static readonly KeyBinding[] Layout =
    [
        new(KeyModifiers.None, 72, "Browse", "NextHeading"),
        new(KeyModifiers.Insert, 84, "Any", "SayTitle"),
        new(KeyModifiers.Insert, 70, "Any", "ReadFormatting"),
    ];

    [Fact]
    public void GesturesFor_ListsEachKeyWithItsMode()
    {
        var editor = new GestureEditor(Layout);

        Assert.Equal([new Gesture(KeyModifiers.None, 72, "Browse")], editor.GesturesFor(NavigationCommand.NextHeading));
        Assert.Equal([new Gesture(KeyModifiers.Insert, 84, "Any")], editor.GesturesFor(NavigationCommand.SayTitle));
        Assert.Equal("Insert+T", editor.GesturesFor(NavigationCommand.SayTitle)[0].Describe());
        Assert.Equal("H (browse mode)", editor.GesturesFor(NavigationCommand.NextHeading)[0].Describe());
    }

    [Fact]
    public void AddingATakenKey_IsAConflict_AndReplacesIt()
    {
        var editor = new GestureEditor(Layout);
        var key = new Gesture(KeyModifiers.Insert, 84, "Any");

        Assert.Equal([NavigationCommand.SayTitle], editor.ConflictsWith(key, NavigationCommand.SayTime));
        Assert.Empty(editor.ConflictsWith(key, NavigationCommand.SayTitle));
        editor.Add(NavigationCommand.SayTime, key);

        Assert.Empty(editor.GesturesFor(NavigationCommand.SayTitle));
        Assert.Equal([key], editor.GesturesFor(NavigationCommand.SayTime));
        Assert.Equal([new KeyBinding(KeyModifiers.Insert, 84, "Any", "SayTime")], editor.UserBindings());
    }

    [Fact]
    public void BindingInOneMode_KeepsTheOtherModesBinding()
    {
        var editor = new GestureEditor(Layout);

        editor.Add(NavigationCommand.SayTime, new Gesture(KeyModifiers.Insert, 84, "Browse"));

        Assert.Equal([new Gesture(KeyModifiers.Insert, 84, "Focus")], editor.GesturesFor(NavigationCommand.SayTitle));
        // Only the changed mode is written; loading it keeps the layout's focus-mode binding
        Assert.Equal([new KeyBinding(KeyModifiers.Insert, 84, "Browse", "SayTime")], editor.UserBindings());
        var reloaded = new GestureEditor(Layout, editor.UserBindings());
        Assert.Equal([new Gesture(KeyModifiers.Insert, 84, "Focus")], reloaded.GesturesFor(NavigationCommand.SayTitle));
    }

    [Fact]
    public void RemovingAKey_WritesNone()
    {
        var editor = new GestureEditor(Layout);

        editor.Remove(NavigationCommand.NextHeading, new Gesture(KeyModifiers.None, 72, "Browse"));

        Assert.Empty(editor.GesturesFor(NavigationCommand.NextHeading));
        Assert.Equal([new KeyBinding(KeyModifiers.None, 72, "Any", "None")], editor.UserBindings());
    }

    [Fact]
    public void UserKeymap_IsLayeredOnTop_AndResetAllForgetsIt()
    {
        var editor = new GestureEditor(Layout, [new KeyBinding(KeyModifiers.None, 74, "Browse", "NextHeading")]);

        Assert.Equal(2, editor.GesturesFor(NavigationCommand.NextHeading).Count);
        Assert.Single(editor.UserBindings());

        editor.ResetAll();
        Assert.Empty(editor.UserBindings());
        Assert.True(editor.IsChanged);
    }

    [Fact]
    public void SavedUserKeymap_LoadsAsTheEditedKeys()
    {
        var layout = KeyMap.LayoutBindings(ConfigDirectory, KeyboardLayout.Desktop);
        var editor = new GestureEditor(layout);
        editor.Add(NavigationCommand.SayTime, new Gesture(KeyModifiers.Insert | KeyModifiers.Shift, 72, "Any"));
        editor.Remove(NavigationCommand.NextHeading, new Gesture(KeyModifiers.None, 72, "Browse"));
        var path = Path.Combine(_directory, KeyMap.UserFileName);
        File.WriteAllText(path, KeyMap.ToJson(editor.UserBindings()));

        var map = KeyMap.LoadLayout(ConfigDirectory, KeyboardLayout.Desktop, path, out var error, out _);

        Assert.Null(error);
        Assert.True(map.TryResolve(KeyModifiers.Insert | KeyModifiers.Shift, 72, InteractionMode.Focus, out var command));
        Assert.Equal(NavigationCommand.SayTime, command);
        Assert.False(map.TryResolve(KeyModifiers.None, 72, InteractionMode.Browse, out _));
        Assert.Equal(KeyMap.ParseBindings(File.ReadAllText(path)), editor.UserBindings());
    }
}
