using Vox.Core.Buffer;
using Xunit;

namespace Vox.Core.Tests.Buffer;

// ---------------------------------------------------------------------------
// Simple mock element for unit testing VBufferBuilder
// ---------------------------------------------------------------------------

internal sealed class MockElement : IVBufferElement
{
    public int[] RuntimeId { get; set; } = [];
    public string Name { get; set; } = string.Empty;
    public string ControlType { get; set; } = "Text";
    public string AriaRole { get; set; } = string.Empty;
    public string AriaProperties { get; set; } = string.Empty;
    public bool IsFocusable { get; set; }

    private readonly List<MockElement> _children = new();
    public IReadOnlyList<IVBufferElement> GetChildren() => _children;

    public MockElement AddChild(MockElement child)
    {
        _children.Add(child);
        return this;
    }
}

// ---------------------------------------------------------------------------
// VBufferBuilderTests
// ---------------------------------------------------------------------------

public class VBufferBuilderTests
{
    private static readonly VBufferBuilder Builder = new();

    // -----------------------------------------------------------------------
    // Helper: build a simple document tree
    //   Document
    //     H1 "Welcome" (ariaRole=heading, ariaProps=level=1)
    //     Link "Click here" (ariaRole=link, isFocusable=true)
    //     Nav landmark (ariaRole=navigation)
    //       Edit "Search" (controlType=Edit, ariaProps=required=true)
    // -----------------------------------------------------------------------

    private static MockElement BuildSimpleTree()
    {
        var root = new MockElement
        {
            RuntimeId = [1],
            Name = "Page",
            ControlType = "Document",
        };

        var h1 = new MockElement
        {
            RuntimeId = [2],
            Name = "Welcome",
            ControlType = "Text",
            AriaRole = "heading",
            AriaProperties = "level=1",
        };

        var link = new MockElement
        {
            RuntimeId = [3],
            Name = "Click here",
            ControlType = "Hyperlink",
            AriaRole = "link",
            IsFocusable = true,
        };

        var nav = new MockElement
        {
            RuntimeId = [4],
            Name = "Site nav",
            ControlType = "Group",
            AriaRole = "navigation",
        };

        var editField = new MockElement
        {
            RuntimeId = [5],
            Name = "Search",
            ControlType = "Edit",
            AriaProperties = "required=true",
            IsFocusable = true,
        };

        nav.AddChild(editField);
        root.AddChild(h1);
        root.AddChild(link);
        root.AddChild(nav);

        return root;
    }

    // -----------------------------------------------------------------------
    // Node tree structure
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_ProducesCorrectNodeCount()
    {
        var doc = Builder.Build(BuildSimpleTree());
        // Document + H1 + Link + Nav + Edit = 5 nodes
        Assert.Equal(5, doc.AllNodes.Count);
    }

    [Fact]
    public void Build_RootIsDocumentNode()
    {
        var doc = Builder.Build(BuildSimpleTree());
        Assert.Equal("Document", doc.Root.ControlType);
        Assert.Equal("Page", doc.Root.Name);
    }

    [Fact]
    public void Build_NodesAreInPreOrder()
    {
        var doc = Builder.Build(BuildSimpleTree());
        var controlTypes = doc.AllNodes.Select(n => n.ControlType).ToList();
        // Pre-order: Document, Text (H1), Hyperlink, Group (Nav), Edit
        Assert.Equal(new[] { "Document", "Text", "Hyperlink", "Group", "Edit" }, controlTypes);
    }

    [Fact]
    public void Build_ParentChildRelationshipsCorrect()
    {
        var doc = Builder.Build(BuildSimpleTree());
        var root = doc.Root;

        Assert.Equal(3, root.Children.Count);
        Assert.All(root.Children, c => Assert.Equal(root, c.Parent));

        // Nav (index 2) has one child (Edit)
        var nav = root.Children[2];
        Assert.Single(nav.Children);
        Assert.Equal(root, nav.Parent);
        Assert.Equal(nav, nav.Children[0].Parent);
    }

    [Fact]
    public void Build_LinkedListIsCorrect()
    {
        var doc = Builder.Build(BuildSimpleTree());
        var nodes = doc.AllNodes;

        // Forward links
        for (int i = 0; i < nodes.Count - 1; i++)
            Assert.Equal(nodes[i + 1], nodes[i].NextInOrder);

        // Backward links
        for (int i = 1; i < nodes.Count; i++)
            Assert.Equal(nodes[i - 1], nodes[i].PrevInOrder);

        Assert.Null(nodes[0].PrevInOrder);
        Assert.Null(nodes[^1].NextInOrder);
    }

    // -----------------------------------------------------------------------
    // Heading level parsing
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_HeadingLevelParsedFromAriaProps()
    {
        var doc = Builder.Build(BuildSimpleTree());
        var h1 = doc.AllNodes.First(n => n.Name == "Welcome");
        Assert.Equal(1, h1.HeadingLevel);
        Assert.True(h1.IsHeading);
    }

    [Theory]
    [InlineData("h1", "", 1)]
    [InlineData("h2", "", 2)]
    [InlineData("h3", "", 3)]
    [InlineData("h4", "", 4)]
    [InlineData("h5", "", 5)]
    [InlineData("h6", "", 6)]
    [InlineData("heading", "level=3", 3)]
    [InlineData("heading", "level=6", 6)]
    [InlineData("", "", 0)]
    [InlineData("link", "", 0)]
    public void Build_HeadingLevel_FromAriaRole(string ariaRole, string ariaProps, int expectedLevel)
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        var node = new MockElement
        {
            RuntimeId = [2],
            Name = "Test heading",
            ControlType = "Text",
            AriaRole = ariaRole,
            AriaProperties = ariaProps,
        };
        root.AddChild(node);

        var doc = Builder.Build(root);
        var built = doc.AllNodes[1]; // index 1 = first child

        Assert.Equal(expectedLevel, built.HeadingLevel);
    }

    // -----------------------------------------------------------------------
    // Landmark detection
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_LandmarkTypeFromAriaRole()
    {
        var doc = Builder.Build(BuildSimpleTree());
        var nav = doc.AllNodes.First(n => n.AriaRole == "navigation");
        Assert.Equal("Navigation", nav.LandmarkType);
        Assert.True(nav.IsLandmark);
    }

    [Theory]
    [InlineData("banner",        "Banner")]
    [InlineData("complementary", "Complementary")]
    [InlineData("contentinfo",   "Content info")]
    [InlineData("form",          "Form")]
    [InlineData("main",          "Main")]
    [InlineData("navigation",    "Navigation")]
    [InlineData("region",        "Region")]
    [InlineData("search",        "Search")]
    [InlineData("link",          "")]
    [InlineData("",              "")]
    public void Build_LandmarkType_AllRoles(string ariaRole, string expectedType)
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        var child = new MockElement
        {
            RuntimeId = [2],
            Name = "Section",
            ControlType = "Group",
            AriaRole = ariaRole,
        };
        root.AddChild(child);

        var doc = Builder.Build(root);
        Assert.Equal(expectedType, doc.AllNodes[1].LandmarkType);
    }

    // -----------------------------------------------------------------------
    // Link detection
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_LinkDetectedFromAriaRole()
    {
        var doc = Builder.Build(BuildSimpleTree());
        var link = doc.AllNodes.First(n => n.Name == "Click here");
        Assert.True(link.IsLink);
    }

    [Theory]
    [InlineData("link",     "Text",      true)]
    [InlineData("a",        "Text",      true)]
    [InlineData("",         "Hyperlink", true)]
    [InlineData("",         "Text",      false)]
    [InlineData("button",   "Button",    false)]
    public void Build_IsLink_Variants(string ariaRole, string controlType, bool expected)
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        var child = new MockElement
        {
            RuntimeId = [2],
            Name = "Element",
            ControlType = controlType,
            AriaRole = ariaRole,
        };
        root.AddChild(child);

        var doc = Builder.Build(root);
        Assert.Equal(expected, doc.AllNodes[1].IsLink);
    }

    // -----------------------------------------------------------------------
    // AriaProperties parsing (required, expanded, visited)
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_IsRequired_ParsedFromAriaProps()
    {
        var doc = Builder.Build(BuildSimpleTree());
        var editNode = doc.AllNodes.First(n => n.Name == "Search");
        Assert.True(editNode.IsRequired);
    }

    [Theory]
    [InlineData("required=true",  true)]
    [InlineData("required=false", false)]
    [InlineData("required=1",     true)]
    [InlineData("required=yes",   true)]
    [InlineData("",               false)]
    public void Build_IsRequired_Variants(string ariaProps, bool expected)
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        var child = new MockElement
        {
            RuntimeId = [2],
            Name = "Field",
            ControlType = "Edit",
            AriaProperties = ariaProps,
        };
        root.AddChild(child);

        var doc = Builder.Build(root);
        Assert.Equal(expected, doc.AllNodes[1].IsRequired);
    }

    [Theory]
    [InlineData("expanded=true",  true,  true)]
    [InlineData("expanded=false", false, false)]
    [InlineData("haspopup=true",  false, true)]
    [InlineData("",               false, false)]
    public void Build_IsExpanded_And_IsExpandable(string ariaProps, bool expectedExpanded, bool expectedExpandable)
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        var child = new MockElement
        {
            RuntimeId = [2],
            Name = "Widget",
            ControlType = "Button",
            AriaProperties = ariaProps,
        };
        root.AddChild(child);

        var doc = Builder.Build(root);
        var node = doc.AllNodes[1];
        Assert.Equal(expectedExpanded, node.IsExpanded);
        Assert.Equal(expectedExpandable, node.IsExpandable);
    }

    [Theory]
    [InlineData("visited=true",  true)]
    [InlineData("visited=false", false)]
    [InlineData("",              false)]
    public void Build_IsVisited_Variants(string ariaProps, bool expected)
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        var child = new MockElement
        {
            RuntimeId = [2],
            Name = "Home",
            ControlType = "Hyperlink",
            AriaRole = "link",
            AriaProperties = ariaProps,
        };
        root.AddChild(child);

        var doc = Builder.Build(root);
        Assert.Equal(expected, doc.AllNodes[1].IsVisited);
    }

    // -----------------------------------------------------------------------
    // Focusability
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("Edit",      false, true)]
    [InlineData("Button",    false, true)]
    [InlineData("CheckBox",  false, true)]
    [InlineData("ComboBox",  false, true)]
    [InlineData("Text",      true,  true)]   // IsFocusable flag
    [InlineData("Text",      false, false)]  // Not focusable
    public void Build_IsFocusable_Variants(string controlType, bool isFocusableFlag, bool expected)
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        var child = new MockElement
        {
            RuntimeId = [2],
            Name = "Element",
            ControlType = controlType,
            IsFocusable = isFocusableFlag,
        };
        root.AddChild(child);

        var doc = Builder.Build(root);
        Assert.Equal(expected, doc.AllNodes[1].IsFocusable);
    }

    [Fact]
    public void Build_LinkIsFocusable_EvenWithoutFlag()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        var link = new MockElement
        {
            RuntimeId = [2],
            Name = "Go",
            ControlType = "Text",
            AriaRole = "link",
            IsFocusable = false,
        };
        root.AddChild(link);

        var doc = Builder.Build(root);
        Assert.True(doc.AllNodes[1].IsFocusable);
    }

    // -----------------------------------------------------------------------
    // Pre-built indices
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_HeadingsIndex_ContainsOnlyHeadings()
    {
        var doc = Builder.Build(BuildSimpleTree());
        Assert.Single(doc.Headings);
        Assert.Equal("Welcome", doc.Headings[0].Name);
    }

    [Fact]
    public void Build_LinksIndex_ContainsOnlyLinks()
    {
        var doc = Builder.Build(BuildSimpleTree());
        Assert.Single(doc.Links);
        Assert.Equal("Click here", doc.Links[0].Name);
    }

    [Fact]
    public void Build_LandmarksIndex_ContainsOnlyLandmarks()
    {
        var doc = Builder.Build(BuildSimpleTree());
        Assert.Single(doc.Landmarks);
        Assert.Equal("Navigation", doc.Landmarks[0].LandmarkType);
    }

    [Fact]
    public void Build_FormFieldsIndex_ContainsEditAndRequired()
    {
        var doc = Builder.Build(BuildSimpleTree());
        // Edit field has ControlType=Edit and ariaProps=required=true — counted as form field
        Assert.Single(doc.FormFields);
        Assert.Equal("Search", doc.FormFields[0].Name);
    }

    [Fact]
    public void Build_FocusableElements_ContainsLinkAndEdit()
    {
        var doc = Builder.Build(BuildSimpleTree());
        var names = doc.FocusableElements.Select(n => n.Name).ToHashSet();
        Assert.Contains("Click here", names);
        Assert.Contains("Search", names);
    }

    // -----------------------------------------------------------------------
    // FlatText construction
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_FlatText_ContainsNodeNames()
    {
        var doc = Builder.Build(BuildSimpleTree());
        // Heading, link, and edit all contribute text; Document and Group (nav) do not
        Assert.Contains("Welcome", doc.FlatText);
        Assert.Contains("Click here", doc.FlatText);
        Assert.Contains("Search", doc.FlatText);
    }

    [Fact]
    public void Build_FlatText_ContainerNodesDoNotContributeText()
    {
        var doc = Builder.Build(BuildSimpleTree());
        // Document and Group/nav containers should not add their names to flat text
        Assert.DoesNotContain("Page", doc.FlatText);
        Assert.DoesNotContain("Site nav", doc.FlatText);
    }

    [Fact]
    public void Build_FlatText_TextRangesAreConsistent()
    {
        var doc = Builder.Build(BuildSimpleTree());
        foreach (var node in doc.AllNodes)
        {
            Assert.True(node.TextRange.Start >= 0);
            Assert.True(node.TextRange.End >= node.TextRange.Start);
            Assert.True(node.TextRange.End <= doc.FlatText.Length);

            if (node.HasText)
            {
                var text = doc.FlatText[node.TextRange.Start..node.TextRange.End];
                Assert.Contains(node.Name, text);
            }
        }
    }

    // -----------------------------------------------------------------------
    // FindByRuntimeId after Build
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_FindByRuntimeId_Works()
    {
        var doc = Builder.Build(BuildSimpleTree());
        var link = doc.FindByRuntimeId([3]);
        Assert.NotNull(link);
        Assert.Equal("Click here", link!.Name);
    }

    // -----------------------------------------------------------------------
    // Empty document (Document with no children)
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_EmptyDocument_ProducesSingleNode()
    {
        var root = new MockElement
        {
            RuntimeId = [99],
            Name = "Empty page",
            ControlType = "Document",
        };

        var doc = Builder.Build(root);
        Assert.Single(doc.AllNodes);
        Assert.Equal("Document", doc.Root.ControlType);
    }

    // -----------------------------------------------------------------------
    // Semicolon-separated AriaProperties
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_AriaPropertiesSemicolonSeparated()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        var child = new MockElement
        {
            RuntimeId = [2],
            Name = "Widget",
            ControlType = "Edit",
            AriaProperties = "required=true;expanded=true;level=2",
        };
        root.AddChild(child);

        var doc = Builder.Build(root);
        var node = doc.AllNodes[1];
        Assert.True(node.IsRequired);
        Assert.True(node.IsExpanded);
    }

    // -----------------------------------------------------------------------
    // ParseAriaPropertyBool and ParseAriaPropertyInt (internal helpers)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("required=true",         "required",  true)]
    [InlineData("required=false",        "required",  false)]
    [InlineData("required=1",            "required",  true)]
    [InlineData("required=yes",          "required",  true)]
    [InlineData("",                      "required",  false)]
    [InlineData("other=true",            "required",  false)]
    [InlineData("a=1;required=true;b=2", "required",  true)]
    public void ParseAriaPropertyBool_Variants(string props, string key, bool expected)
    {
        Assert.Equal(expected, VBufferBuilder.ParseAriaPropertyBool(props, key));
    }

    [Theory]
    [InlineData("level=3",      "level", 3)]
    [InlineData("level=6",      "level", 6)]
    [InlineData("",             "level", 0)]
    [InlineData("other=5",      "level", 0)]
    [InlineData("a=1;level=2",  "level", 2)]
    public void ParseAriaPropertyInt_Variants(string props, string key, int expected)
    {
        Assert.Equal(expected, VBufferBuilder.ParseAriaPropertyInt(props, key));
    }
}
