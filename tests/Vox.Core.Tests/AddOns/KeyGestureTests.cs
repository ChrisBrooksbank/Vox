using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Sdk;
using Xunit;

namespace Vox.Core.Tests.AddOns;

public class KeyGestureTests
{
    [Theory]
    [InlineData("VoxKey+Shift+J", GestureModifiers.VoxKey | GestureModifiers.Shift, 0x4A)]
    [InlineData("insert+ctrl+f12", GestureModifiers.VoxKey | GestureModifiers.Ctrl, 0x7B)]
    [InlineData("Alt+PageDown", GestureModifiers.Alt, 0x22)]
    [InlineData("NumPad5", GestureModifiers.None, 0x65)]
    [InlineData("Comma", GestureModifiers.None, 0xBC)]
    [InlineData("Ctrl+7", GestureModifiers.Ctrl, 0x37)]
    [InlineData("Insert", GestureModifiers.None, 0x2D)]
    public void Gestures_AreParsed(string text, GestureModifiers modifiers, int vk)
    {
        var gesture = KeyGesture.Parse(text);

        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(vk, gesture.VirtualKey);
        Assert.Equal(gesture, KeyGesture.Parse(gesture.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Hyper+J")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+")]
    public void NonGestures_AreNot(string text)
    {
        Assert.False(KeyGesture.TryParse(text, out _));
        Assert.Throws<FormatException>(() => KeyGesture.Parse(text));
    }

    [Fact]
    public void TheModifiers_AreVoxsOwn() =>
        Assert.Equal(KeyModifiers.Insert | KeyModifiers.Shift | KeyModifiers.Ctrl | KeyModifiers.Alt,
            (KeyModifiers)(int)(GestureModifiers.VoxKey | GestureModifiers.Shift | GestureModifiers.Ctrl | GestureModifiers.Alt));

    [Fact]
    public void TheKeyLayer_KeepsTheFirstCommandOnAKey_AndHonoursModes()
    {
        var first = new AddOnCommandRef("a", "one", "One", "");
        var second = new AddOnCommandRef("b", "two", "Two", "");
        var browse = new AddOnCommandRef("a", "browse", "Browse", "");

        var layer = AddOnKeyLayer.Build([
            (KeyGesture.Parse("VoxKey+J"), first),
            (KeyGesture.Parse("VoxKey+J"), second),
            (KeyGesture.Parse("J", GestureMode.Browse), browse),
        ]);

        Assert.Equal(2, layer.Count);
        Assert.True(layer.TryResolveOutsideDocument(KeyModifiers.Insert, 0x4A, out var outside));
        Assert.Same(first, outside);
        Assert.True(layer.TryResolve(KeyModifiers.Insert, 0x4A, InteractionMode.Focus, out _));
        Assert.True(layer.TryResolve(KeyModifiers.None, 0x4A, InteractionMode.Browse, out var browsed));
        Assert.Same(browse, browsed);
        Assert.False(layer.TryResolve(KeyModifiers.None, 0x4A, InteractionMode.Focus, out _));
        Assert.False(layer.TryResolveOutsideDocument(KeyModifiers.None, 0x4A, out _));
    }
}
