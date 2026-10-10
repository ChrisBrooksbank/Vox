using Moq;
using Vox.Core.Accessibility;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class ElementActivationTests
{
    private sealed class Target(bool hasPattern, (int X, int Y)? clickPoint, bool canFocus) : INavigatorObject
    {
        public List<string> Calls { get; } = [];
        public INavigatorObject? GetParent() => null;
        public INavigatorObject? GetFirstChild() => null;
        public INavigatorObject? GetLastChild() => null;
        public INavigatorObject? GetNextSibling() => null;
        public INavigatorObject? GetPreviousSibling() => null;
        public FocusChangedEvent Describe() => new(DateTimeOffset.UtcNow, "target", "Text");
        public bool Activate() { Calls.Add("activate"); return hasPattern; }
        public void ScrollIntoView() => Calls.Add("scroll");
        public (int X, int Y)? GetClickPoint() { Calls.Add("point"); return clickPoint; }
        public bool SetFocus() { Calls.Add("focus"); return canFocus; }
    }

    [Fact]
    public void Pattern_IsUsedWithoutTouchingTheMouse()
    {
        var mouse = new Mock<IMouseInput>(MockBehavior.Strict);
        var target = new Target(hasPattern: true, clickPoint: (10, 20), canFocus: true);

        Assert.Equal(ActivationOutcome.Pattern, ElementActivation.Activate(target, mouse.Object));
        Assert.Equal(["activate"], target.Calls);
    }

    [Fact]
    public void NoPattern_ClicksAtTheClickablePoint_AndPutsThePointerBack()
    {
        var mouse = new Mock<IMouseInput>();
        mouse.SetupGet(m => m.Position).Returns((500, 600));
        var calls = new List<string>();
        mouse.Setup(m => m.MoveTo(It.IsAny<int>(), It.IsAny<int>())).Callback<int, int>((x, y) => calls.Add($"move {x},{y}"));
        mouse.Setup(m => m.Press(MouseButton.Left)).Callback(() => calls.Add("down"));
        mouse.Setup(m => m.Release(MouseButton.Left)).Callback(() => calls.Add("up"));
        var target = new Target(hasPattern: false, clickPoint: (10, 20), canFocus: true);

        Assert.Equal(ActivationOutcome.Clicked, ElementActivation.Activate(target, mouse.Object));
        Assert.Equal(["activate", "scroll", "point"], target.Calls);
        Assert.Equal(["move 10,20", "down", "up", "move 500,600"], calls);
    }

    [Fact]
    public void NoPatternOrLocation_Focuses()
    {
        var mouse = new Mock<IMouseInput>();
        var target = new Target(hasPattern: false, clickPoint: null, canFocus: true);

        Assert.Equal(ActivationOutcome.Focused, ElementActivation.Activate(target, mouse.Object));
        mouse.Verify(m => m.Press(It.IsAny<MouseButton>()), Times.Never);
    }

    [Fact]
    public void NoMouse_TriesFocusOnly()
    {
        var target = new Target(hasPattern: false, clickPoint: (1, 1), canFocus: false);

        Assert.Equal(ActivationOutcome.None, ElementActivation.Activate(target, mouse: null));
        Assert.Equal(["activate", "focus"], target.Calls);
    }
}
