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

public class ObjectNavigatorSimpleReviewTests
{
    // Window "Settings"
    //   (pane)
    //     (group): Wi-Fi, Bluetooth
    //     (group)            <- empty layout object
    //   "Advanced" group: Proxy
    //   OK
    private readonly MockNavigatorObject _window = new("Settings", "Window",
        new MockNavigatorObject("", "Pane",
            new MockNavigatorObject("", "Group",
                new MockNavigatorObject("Wi-Fi", "CheckBox"),
                new MockNavigatorObject("Bluetooth", "CheckBox")),
            new MockNavigatorObject("", "Group")),
        new MockNavigatorObject("Advanced", "Group",
            new MockNavigatorObject("Proxy", "Edit")),
        new MockNavigatorObject("OK"));

    private ObjectNavigator NavigatorAt(string name, bool simple = true)
    {
        var navigator = new ObjectNavigator { SimpleReview = simple };
        navigator.MoveTo(_window.Find(name));
        return navigator;
    }

    [Fact]
    public void FirstChild_LooksThroughLayoutObjects()
    {
        var navigator = NavigatorAt("Settings");

        Assert.Same(_window.Find("Wi-Fi"), navigator.Move(NavigatorMove.FirstChild));
    }

    [Fact]
    public void Parent_SkipsLayoutAncestors()
    {
        var navigator = NavigatorAt("Bluetooth");

        Assert.Same(_window, navigator.Move(NavigatorMove.Parent));
    }

    [Fact]
    public void Next_LeavesLayoutParentsAndSkipsEmptyOnes()
    {
        var navigator = NavigatorAt("Bluetooth");

        Assert.Same(_window.Find("Advanced"), navigator.Move(NavigatorMove.Next));
        Assert.Same(_window.Find("OK"), navigator.Move(NavigatorMove.Next));
        Assert.Null(navigator.Move(NavigatorMove.Next));
    }

    [Fact]
    public void Previous_EntersLayoutSiblingsFromTheEnd()
    {
        var navigator = NavigatorAt("Advanced");

        Assert.Same(_window.Find("Bluetooth"), navigator.Move(NavigatorMove.Previous));
        Assert.Same(_window.Find("Wi-Fi"), navigator.Move(NavigatorMove.Previous));
        Assert.Null(navigator.Move(NavigatorMove.Previous));
    }

    [Fact]
    public void NamedGroups_AreNotSkipped()
    {
        var navigator = NavigatorAt("Proxy");

        Assert.Same(_window.Find("Advanced"), navigator.Move(NavigatorMove.Parent));
    }

    [Fact]
    public void WithoutSimpleReview_LayoutObjectsAreVisited()
    {
        var navigator = NavigatorAt("Settings", simple: false);

        var pane = navigator.Move(NavigatorMove.FirstChild);

        Assert.Equal("Pane", pane?.Describe().ControlType);
    }

    [Fact]
    public void LayoutRoot_IsNotSkipped()
    {
        var root = new MockNavigatorObject("", "Pane", new MockNavigatorObject("OK"));
        var navigator = new ObjectNavigator { SimpleReview = true };
        navigator.MoveTo(root.Find("OK"));

        Assert.Same(root, navigator.Move(NavigatorMove.Parent));
    }

    [Fact]
    public void DeeplyNestedEmptyLayout_GivesUpInsteadOfHanging()
    {
        var deepest = new MockNavigatorObject("", "Group");
        var node = deepest;
        for (int i = 0; i < 5000; i++)
            node = new MockNavigatorObject("", "Group", node);
        var root = new MockNavigatorObject("Window", "Window", node);
        var navigator = new ObjectNavigator { SimpleReview = true };
        navigator.MoveTo(root);

        Assert.Null(navigator.Move(NavigatorMove.FirstChild));
        Assert.Same(root, navigator.Current);
    }
}
