using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Navigation;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class StateAnnouncementTests
{
    private static string Announce(VBufferNode node) => new AnnouncementBuilder().Build(node, VerbosityLevel.Beginner);

    [Fact]
    public void CollapsedDisclosure_IsAnnouncedCollapsed()
    {
        var node = new VBufferNode { Name = "Details", ControlType = "Button", IsExpandable = true, IsExpanded = false };
        Assert.Equal("Details, button, collapsed", Announce(node));
    }

    [Theory]
    [InlineData(0, "Subscribe, check box, not checked")]
    [InlineData(1, "Subscribe, check box, checked")]
    [InlineData(2, "Subscribe, check box, half checked")]
    public void CheckBox_AnnouncesToggleState(int toggle, string expected)
    {
        var node = new VBufferNode { Name = "Subscribe", ControlType = "CheckBox", ToggleState = toggle };
        Assert.Equal(expected, Announce(node));
    }

    [Fact]
    public void RadioButton_AnnouncesCheckedFromSelection()
    {
        Assert.Equal("Small, radio button, checked",
            Announce(new VBufferNode { Name = "Small", ControlType = "RadioButton", IsSelected = true }));
        Assert.Equal("Large, radio button, not checked",
            Announce(new VBufferNode { Name = "Large", ControlType = "RadioButton", IsSelected = false }));
    }

    [Fact]
    public void SelectedTab_IsAnnouncedSelected_UnselectedSaysNothing()
    {
        Assert.Equal("Home, tab, selected", Announce(new VBufferNode { Name = "Home", ControlType = "TabItem", IsSelected = true }));
        Assert.Equal("News, tab", Announce(new VBufferNode { Name = "News", ControlType = "TabItem", IsSelected = false }));
    }

    [Theory]
    [InlineData(0, null, true, false)]      // Collapsed
    [InlineData(1, null, true, true)]       // Expanded
    [InlineData(3, "expanded=true", false, false)] // LeafNode wins over ARIA
    [InlineData(null, "expanded=false", true, false)]
    [InlineData(null, "", false, false)]
    public void ControlState_Expansion(int? uiaState, string? aria, bool expandable, bool expanded)
    {
        Assert.Equal((expandable, expanded), ControlState.Expansion(uiaState, aria, "Button"));
    }

    [Fact]
    public void Builder_UsesUiaStates()
    {
        var root = new Vox.Core.Tests.Buffer.MockElement { RuntimeId = [1], ControlType = "Document" };
        root.AddChild(new Vox.Core.Tests.Buffer.MockElement { RuntimeId = [2], Name = "Menu", ControlType = "Button", ExpandCollapseState = 0 });
        root.AddChild(new Vox.Core.Tests.Buffer.MockElement { RuntimeId = [3], Name = "Agree", ControlType = "CheckBox", ToggleState = 1 });

        var doc = new VBufferBuilder().Build(root);

        Assert.True(doc.FindByRuntimeId([2])!.IsExpandable);
        Assert.False(doc.FindByRuntimeId([2])!.IsExpanded);
        Assert.Equal(1, doc.FindByRuntimeId([3])!.ToggleState);
    }
}
