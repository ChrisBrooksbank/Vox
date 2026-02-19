using Vox.Core.Buffer;
using Xunit;

namespace Vox.Core.Tests.Buffer;

public class VBufferDocumentTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static VBufferNode MakeNode(int id, int[] runtimeId, string name,
        string controlType = "Text", int headingLevel = 0, bool isLink = false,
        bool isLandmark = false, string landmarkType = "", bool isFocusable = false,
        bool isRequired = false, bool isExpandable = false,
        (int Start, int End) textRange = default)
    {
        return new VBufferNode
        {
            Id = id,
            UIARuntimeId = runtimeId,
            Name = name,
            ControlType = controlType,
            HeadingLevel = headingLevel,
            IsLink = isLink,
            LandmarkType = isLandmark ? (string.IsNullOrEmpty(landmarkType) ? "main" : landmarkType) : landmarkType,
            IsFocusable = isFocusable,
            IsRequired = isRequired,
            IsExpandable = isExpandable,
            TextRange = textRange,
        };
    }

    private static VBufferDocument BuildSimpleDoc()
    {
        // FlatText: "Hello\nWorld\n"
        //  Node 0: Document  (0, 12)  - root
        //  Node 1: Heading1  (0, 5)   "Hello" H1
        //  Node 2: Link      (6, 11)  "World" link
        //  Node 3: Nav       (11, 12) landmark
        var root = MakeNode(0, [0], "doc", "Document", textRange: (0, 12));
        var h1   = MakeNode(1, [1], "Hello", headingLevel: 1, textRange: (0, 5));
        var link = MakeNode(2, [2], "World", isLink: true, isFocusable: true, textRange: (6, 11));
        var nav  = MakeNode(3, [3], "nav", isLandmark: true, landmarkType: "navigation", textRange: (11, 12));

        // Link order in traversal
        h1.PrevInOrder = root; h1.NextInOrder = link;
        link.PrevInOrder = h1; link.NextInOrder = nav;
        nav.PrevInOrder = link;

        root.Children.Add(h1);
        root.Children.Add(link);
        root.Children.Add(nav);
        h1.Parent = root; link.Parent = root; nav.Parent = root;

        var allNodes = new List<VBufferNode> { root, h1, link, nav };
        return new VBufferDocument("Hello\nWorld\n", root, allNodes);
    }

    // -------------------------------------------------------------------------
    // Index correctness
    // -------------------------------------------------------------------------

    [Fact]
    public void Headings_ContainsOnlyHeadingNodes()
    {
        var doc = BuildSimpleDoc();
        Assert.Single(doc.Headings);
        Assert.Equal("Hello", doc.Headings[0].Name);
    }

    [Fact]
    public void Links_ContainsOnlyLinkNodes()
    {
        var doc = BuildSimpleDoc();
        Assert.Single(doc.Links);
        Assert.Equal("World", doc.Links[0].Name);
    }

    [Fact]
    public void Landmarks_ContainsOnlyLandmarkNodes()
    {
        var doc = BuildSimpleDoc();
        Assert.Single(doc.Landmarks);
        Assert.Equal("navigation", doc.Landmarks[0].LandmarkType);
    }

    [Fact]
    public void FocusableElements_ContainsFocusableNodes()
    {
        var doc = BuildSimpleDoc();
        Assert.Single(doc.FocusableElements);
        Assert.Equal("World", doc.FocusableElements[0].Name);
    }

    [Fact]
    public void FormFields_ContainsEditAndRequiredNodes()
    {
        var edit = MakeNode(10, [10], "Name", controlType: "Edit", isFocusable: true, textRange: (0, 4));
        var required = MakeNode(11, [11], "Email", isRequired: true, textRange: (5, 10));
        var root = MakeNode(0, [0], "doc", "Document", textRange: (0, 10));
        root.Children.Add(edit); root.Children.Add(required);
        edit.Parent = root; required.Parent = root;

        var doc = new VBufferDocument("Name Email", root, new List<VBufferNode> { root, edit, required });

        Assert.Equal(2, doc.FormFields.Count);
    }

    // -------------------------------------------------------------------------
    // FindByRuntimeId
    // -------------------------------------------------------------------------

    [Fact]
    public void FindByRuntimeId_ReturnsCorrectNode()
    {
        var doc = BuildSimpleDoc();
        var node = doc.FindByRuntimeId([2]);
        Assert.NotNull(node);
        Assert.Equal("World", node!.Name);
    }

    [Fact]
    public void FindByRuntimeId_ReturnsNull_WhenNotFound()
    {
        var doc = BuildSimpleDoc();
        var node = doc.FindByRuntimeId([999]);
        Assert.Null(node);
    }

    [Fact]
    public void FindByRuntimeId_WorksWithMultiPartId()
    {
        var root = MakeNode(0, [0], "doc", "Document", textRange: (0, 5));
        var child = MakeNode(1, [42, 7, 3], "Child", textRange: (0, 5));
        root.Children.Add(child); child.Parent = root;
        var doc = new VBufferDocument("Hello", root, new List<VBufferNode> { root, child });

        var found = doc.FindByRuntimeId([42, 7, 3]);
        Assert.NotNull(found);
        Assert.Equal("Child", found!.Name);
    }

    // -------------------------------------------------------------------------
    // FindNodeAtOffset
    // -------------------------------------------------------------------------

    [Fact]
    public void FindNodeAtOffset_ReturnsNodeCoveringOffset()
    {
        var doc = BuildSimpleDoc();
        // "Hello\nWorld\n"
        //  0-4: h1, 6-10: link
        var node = doc.FindNodeAtOffset(0);
        Assert.NotNull(node);
        // root covers 0-11, h1 covers 0-4; h1 should win (more specific, comes first in scan)
        Assert.Equal(1, node!.HeadingLevel);
    }

    [Fact]
    public void FindNodeAtOffset_ReturnsLinkNodeAtLinkRange()
    {
        var doc = BuildSimpleDoc();
        var node = doc.FindNodeAtOffset(7); // inside "World" (6-10)
        Assert.NotNull(node);
        Assert.True(node!.IsLink);
    }

    [Fact]
    public void FindNodeAtOffset_ReturnsNull_ForNegativeOffset()
    {
        var doc = BuildSimpleDoc();
        Assert.Null(doc.FindNodeAtOffset(-1));
    }

    [Fact]
    public void FindNodeAtOffset_ReturnsNull_ForOffsetBeyondText()
    {
        var doc = BuildSimpleDoc();
        Assert.Null(doc.FindNodeAtOffset(100));
    }

    // -------------------------------------------------------------------------
    // AllNodes order
    // -------------------------------------------------------------------------

    [Fact]
    public void AllNodes_AreInDocumentOrder()
    {
        var doc = BuildSimpleDoc();
        var ids = doc.AllNodes.Select(n => n.Id).ToList();
        Assert.Equal(new[] { 0, 1, 2, 3 }, ids);
    }

    // -------------------------------------------------------------------------
    // FlatText
    // -------------------------------------------------------------------------

    [Fact]
    public void FlatText_IsPreservedAsProvided()
    {
        var doc = BuildSimpleDoc();
        Assert.Equal("Hello\nWorld\n", doc.FlatText);
    }
}
