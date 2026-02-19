using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Navigation;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class AnnouncementBuilderTests
{
    private readonly AnnouncementBuilder _builder = new();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static VBufferNode MakeNode(
        string name = "Test",
        string controlType = "",
        int headingLevel = 0,
        string landmarkType = "",
        bool isLink = false,
        bool isVisited = false,
        bool isRequired = false,
        bool isExpandable = false,
        bool isExpanded = false)
    {
        return new VBufferNode
        {
            Id = 1,
            UIARuntimeId = [1],
            Name = name,
            ControlType = controlType,
            HeadingLevel = headingLevel,
            LandmarkType = landmarkType,
            IsLink = isLink,
            IsVisited = isVisited,
            IsRequired = isRequired,
            IsExpandable = isExpandable,
            IsExpanded = isExpanded,
        };
    }

    // -------------------------------------------------------------------------
    // Beginner verbosity — everything announced
    // -------------------------------------------------------------------------

    [Fact]
    public void Beginner_PlainTextNode_ReturnsName()
    {
        var node = MakeNode(name: "Hello world", controlType: "Text");
        var result = _builder.Build(node, VerbosityLevel.Beginner);
        Assert.Contains("Hello world", result);
    }

    [Fact]
    public void Beginner_HeadingNode_AnnouncesLevelAndName()
    {
        var node = MakeNode(name: "Products", controlType: "Heading", headingLevel: 2);
        var result = _builder.Build(node, VerbosityLevel.Beginner);
        Assert.Contains("heading level 2", result);
        Assert.Contains("Products", result);
    }

    [Fact]
    public void Beginner_HeadingNode_DoesNotDuplicateHeadingControlType()
    {
        var node = MakeNode(name: "Products", controlType: "Heading", headingLevel: 2);
        var result = _builder.Build(node, VerbosityLevel.Beginner);
        // Should NOT say "heading level 2, Products, heading" — avoid the redundant "heading"
        var occurrences = CountOccurrences(result, "heading");
        Assert.Equal(1, occurrences); // only "heading level 2"
    }

    [Fact]
    public void Beginner_LandmarkNode_AnnouncesLandmarkType()
    {
        var node = MakeNode(name: "Site navigation", landmarkType: "navigation", controlType: "Group");
        var result = _builder.Build(node, VerbosityLevel.Beginner);
        Assert.Contains("navigation landmark", result);
        Assert.Contains("Site navigation", result);
    }

    [Fact]
    public void Beginner_VisitedLink_AnnouncesVisited()
    {
        var node = MakeNode(name: "Home", controlType: "Hyperlink", isLink: true, isVisited: true);
        var result = _builder.Build(node, VerbosityLevel.Beginner);
        Assert.Contains("visited", result);
    }

    [Fact]
    public void Beginner_UnvisitedLink_DoesNotAnnounceVisited()
    {
        var node = MakeNode(name: "Home", controlType: "Hyperlink", isLink: true, isVisited: false);
        var result = _builder.Build(node, VerbosityLevel.Beginner);
        Assert.DoesNotContain("visited", result);
    }

    [Fact]
    public void Beginner_RequiredField_AnnouncesRequired()
    {
        var node = MakeNode(name: "Email", controlType: "Edit", isRequired: true);
        var result = _builder.Build(node, VerbosityLevel.Beginner);
        Assert.Contains("required", result);
    }

    [Fact]
    public void Beginner_ExpandedComboBox_AnnouncesExpanded()
    {
        var node = MakeNode(name: "Country", controlType: "ComboBox", isExpandable: true, isExpanded: true);
        var result = _builder.Build(node, VerbosityLevel.Beginner);
        Assert.Contains("expanded", result);
    }

    [Fact]
    public void Beginner_CollapsedComboBox_AnnouncesCollapsed()
    {
        var node = MakeNode(name: "Country", controlType: "ComboBox", isExpandable: true, isExpanded: false);
        var result = _builder.Build(node, VerbosityLevel.Beginner);
        Assert.Contains("collapsed", result);
    }

    [Fact]
    public void Beginner_NonExpandableNode_DoesNotAnnounceExpandedState()
    {
        var node = MakeNode(name: "Button", controlType: "Button", isExpandable: false);
        var result = _builder.Build(node, VerbosityLevel.Beginner);
        Assert.DoesNotContain("expanded", result);
        Assert.DoesNotContain("collapsed", result);
    }

    // -------------------------------------------------------------------------
    // Intermediate verbosity — control type + essential state, no landmark/position
    // -------------------------------------------------------------------------

    [Fact]
    public void Intermediate_LandmarkNode_DoesNotAnnounceLandmark()
    {
        var node = MakeNode(name: "Site navigation", landmarkType: "navigation", controlType: "Group");
        var result = _builder.Build(node, VerbosityLevel.Intermediate);
        Assert.DoesNotContain("landmark", result);
    }

    [Fact]
    public void Intermediate_HeadingNode_AnnouncesLevelAndName()
    {
        var node = MakeNode(name: "Products", controlType: "Heading", headingLevel: 3);
        var result = _builder.Build(node, VerbosityLevel.Intermediate);
        Assert.Contains("heading level 3", result);
        Assert.Contains("Products", result);
    }

    [Fact]
    public void Intermediate_VisitedLink_AnnouncesVisited()
    {
        var node = MakeNode(name: "About", controlType: "Hyperlink", isLink: true, isVisited: true);
        var result = _builder.Build(node, VerbosityLevel.Intermediate);
        Assert.Contains("visited", result);
    }

    [Fact]
    public void Intermediate_RequiredField_AnnouncesRequired()
    {
        var node = MakeNode(name: "Name", controlType: "Edit", isRequired: true);
        var result = _builder.Build(node, VerbosityLevel.Intermediate);
        Assert.Contains("required", result);
    }

    // -------------------------------------------------------------------------
    // Advanced verbosity — minimal: just name, plus expanded state
    // -------------------------------------------------------------------------

    [Fact]
    public void Advanced_PlainLink_ReturnsOnlyName()
    {
        var node = MakeNode(name: "Products", controlType: "Hyperlink", isLink: true, isVisited: true);
        var result = _builder.Build(node, VerbosityLevel.Advanced);
        Assert.Equal("Products", result);
    }

    [Fact]
    public void Advanced_HeadingNode_DoesNotAnnounceHeadingLevel()
    {
        var node = MakeNode(name: "Products", controlType: "Heading", headingLevel: 2);
        var result = _builder.Build(node, VerbosityLevel.Advanced);
        Assert.DoesNotContain("heading", result);
        Assert.Contains("Products", result);
    }

    [Fact]
    public void Advanced_ExpandableNode_StillAnnouncesExpandedState()
    {
        var node = MakeNode(name: "Menu", controlType: "MenuItem", isExpandable: true, isExpanded: false);
        var result = _builder.Build(node, VerbosityLevel.Advanced);
        Assert.Contains("collapsed", result);
    }

    [Fact]
    public void Advanced_RequiredField_DoesNotAnnounceRequired()
    {
        var node = MakeNode(name: "Email", controlType: "Edit", isRequired: true);
        var result = _builder.Build(node, VerbosityLevel.Advanced);
        Assert.DoesNotContain("required", result);
    }

    // -------------------------------------------------------------------------
    // Edge cases
    // -------------------------------------------------------------------------

    [Fact]
    public void EmptyName_ReturnsOnlyPresentFields()
    {
        var node = MakeNode(name: "", controlType: "Button");
        var result = _builder.Build(node, VerbosityLevel.Beginner);
        // Name is empty so only control type is announced
        Assert.Contains("button", result);
    }

    [Fact]
    public void NodeWithNoContent_ReturnsEmptyString()
    {
        var node = MakeNode(name: "", controlType: "");
        var result = _builder.Build(node, VerbosityLevel.Advanced);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ProfileOverload_ProducesCorrectOutput()
    {
        var node = MakeNode(name: "Home", controlType: "Hyperlink", isLink: true, isVisited: true);
        var profile = VerbosityProfile.Beginner;
        var result = _builder.Build(node, profile);
        Assert.Contains("visited", result);
        Assert.Contains("Home", result);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
