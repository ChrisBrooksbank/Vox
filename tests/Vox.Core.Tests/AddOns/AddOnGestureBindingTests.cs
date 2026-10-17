using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Sdk;
using Xunit;

namespace Vox.Core.Tests.AddOns;

public class AddOnGestureBindingTests
{
    private static readonly AddOnCommandRef Totals = new("acme", "totals", "Read totals", "");
    private static readonly AddOnCommandRef Other = new("acme", "other", "Other", "");

    private static (KeyGesture, AddOnCommandRef)[] Defaults => [(KeyGesture.Parse("VoxKey+J"), Totals)];

    [Fact]
    public void TheBindingName_NamesTheAddOnAndCommand() =>
        Assert.Equal("AddOn:acme/totals", Totals.BindingName);

    [Fact]
    public void TheEditor_ListsAnAddOnCommandsDefaultKeys_AndSavesNothingUntilChanged()
    {
        var editor = new GestureEditor(AddOnKeyLayer.DefaultBindings(Defaults));

        var keys = editor.GesturesFor(Totals.BindingName);

        Assert.Single(keys);
        Assert.Equal(0x4A, keys[0].VkCode);
        Assert.Empty(editor.UserBindings());
    }

    [Fact]
    public void ARebinding_IsKeptInTheUserKeymap_AndReplacesTheDefaultKey()
    {
        var editor = new GestureEditor(AddOnKeyLayer.DefaultBindings(Defaults));
        editor.Remove(Totals.BindingName, editor.GesturesFor(Totals.BindingName)[0]);
        editor.Add(Totals.BindingName, new Gesture(KeyModifiers.Ctrl, 0x4B, "Any"));
        var saved = KeyMap.ParseBindings(KeyMap.ToJson(editor.UserBindings()));

        var effective = AddOnKeyLayer.ApplyUserKeys(Defaults, [Totals], saved);

        var (gesture, command) = Assert.Single(effective);
        Assert.Equal(GestureModifiers.Ctrl, gesture.Modifiers);
        Assert.Equal(0x4B, gesture.VirtualKey);
        Assert.Same(Totals, command);
    }

    [Fact]
    public void ACommandWithoutADefaultKey_CanBeGivenOne()
    {
        var user = new[] { new KeyBinding(KeyModifiers.Alt, 0x4C, "Any", Other.BindingName) };

        var effective = AddOnKeyLayer.ApplyUserKeys(Defaults, [Totals, Other], user);

        Assert.Contains(effective, e => e.Command == Other && e.Gesture.VirtualKey == 0x4C);
        Assert.Contains(effective, e => e.Command == Totals && e.Gesture.VirtualKey == 0x4A);
    }

    [Fact]
    public void AKeyTakenFromAnotherAddOnCommand_IsReported_AndMoved()
    {
        var editor = new GestureEditor(AddOnKeyLayer.DefaultBindings(Defaults));
        var gesture = editor.GesturesFor(Totals.BindingName)[0];

        Assert.Equal([Totals.BindingName], editor.ConflictNames(gesture, Other.BindingName));
        editor.Add(Other.BindingName, gesture);

        Assert.Empty(editor.GesturesFor(Totals.BindingName));
        Assert.Single(editor.GesturesFor(Other.BindingName));
    }

    [Fact]
    public void WithoutUserEntries_TheDefaultsStand() =>
        Assert.Equal(Defaults, AddOnKeyLayer.ApplyUserKeys(Defaults, [Totals], []));

    [Fact]
    public void AddOnEntries_AreNotWarnedAboutByTheKeyMap()
    {
        var json = KeyMap.ToJson([new KeyBinding(KeyModifiers.Ctrl, 0x4B, "Any", Totals.BindingName)]);

        var map = KeyMap.LoadFromJson(json, out var warnings);

        Assert.Empty(warnings);
        Assert.False(map.TryResolve(KeyModifiers.Ctrl, 0x4B, InteractionMode.Browse, out _));
    }
}
