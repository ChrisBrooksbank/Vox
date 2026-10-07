using Vox.Core.Navigation;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class ObjectNavigatorTests
{
    // Window
    //   Toolbar: Open, Save
    //   Edit
    //   Status bar: Ln 1
    private readonly MockNavigatorObject _window = new("Notepad", "Window",
        new MockNavigatorObject("Toolbar", "ToolBar",
            new MockNavigatorObject("Open"),
            new MockNavigatorObject("Save")),
        new MockNavigatorObject("Text editor", "Edit"),
        new MockNavigatorObject("Status bar", "StatusBar",
            new MockNavigatorObject("Ln 1", "Text")));

    private ObjectNavigator NavigatorAt(string name)
    {
        var navigator = new ObjectNavigator();
        navigator.MoveTo(_window.Find(name));
        return navigator;
    }

    [Fact]
    public void Next_MovesToNextSibling()
    {
        var navigator = NavigatorAt("Text editor");

        Assert.Equal("Status bar", navigator.Move(NavigatorMove.Next)?.Describe().ElementName);
        Assert.Same(_window.Find("Status bar"), navigator.Current);
    }

    [Fact]
    public void Previous_MovesToPreviousSibling()
    {
        var navigator = NavigatorAt("Text editor");

        Assert.Same(_window.Find("Toolbar"), navigator.Move(NavigatorMove.Previous));
    }

    [Fact]
    public void FirstChild_MovesIntoTheObject()
    {
        var navigator = NavigatorAt("Toolbar");

        Assert.Same(_window.Find("Open"), navigator.Move(NavigatorMove.FirstChild));
    }

    [Fact]
    public void Parent_MovesOut()
    {
        var navigator = NavigatorAt("Ln 1");

        Assert.Same(_window.Find("Status bar"), navigator.Move(NavigatorMove.Parent));
        Assert.Same(_window, navigator.Move(NavigatorMove.Parent));
    }

    [Theory]
    [InlineData("Status bar", NavigatorMove.Next)]
    [InlineData("Toolbar", NavigatorMove.Previous)]
    [InlineData("Save", NavigatorMove.FirstChild)]
    [InlineData("Notepad", NavigatorMove.Parent)]
    public void Boundary_ReturnsNullAndStaysPut(string start, NavigatorMove move)
    {
        var navigator = NavigatorAt(start);

        Assert.Null(navigator.Move(move));
        Assert.Same(_window.Find(start), navigator.Current);
    }

    [Fact]
    public void NoNavigatorObject_IsABoundary()
    {
        var navigator = new ObjectNavigator();

        Assert.Null(navigator.Move(NavigatorMove.Next));
        Assert.Null(navigator.Current);
    }
}
