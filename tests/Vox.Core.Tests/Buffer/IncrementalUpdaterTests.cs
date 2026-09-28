using Vox.Core.Buffer;
using Xunit;

namespace Vox.Core.Tests.Buffer;

/// <summary>
/// Unit tests for <see cref="IncrementalUpdater"/>.
///
/// All tests build a simple document, apply an incremental update, and then
/// verify that the resulting <see cref="VBufferDocument"/> is correct.
///
/// Document structure used in most tests:
///   Document "Page"
///     H1 "Welcome"            (ariaRole=heading, level=1)
///     Link "Click here"       (ariaRole=link)
///     Group "Nav"             (ariaRole=navigation)
///       Edit "Search"         (required=true)
/// </summary>
public class IncrementalUpdaterTests
{
    // -----------------------------------------------------------------------
    // Helpers – reuse MockElement from VBufferBuilderTests (same namespace, same assembly)
    // -----------------------------------------------------------------------

    private static VBufferDocument BuildBaseDocument()
    {
        var root = new MockElement { RuntimeId = [1], Name = "Page",       ControlType = "Document" };
        var h1   = new MockElement { RuntimeId = [2], Name = "Welcome",    ControlType = "Text",      AriaRole = "heading", AriaProperties = "level=1" };
        var link = new MockElement { RuntimeId = [3], Name = "Click here", ControlType = "Hyperlink", AriaRole = "link",    IsFocusable = true };
        var nav  = new MockElement { RuntimeId = [4], Name = "Nav",        ControlType = "Group",     AriaRole = "navigation" };
        var edit = new MockElement { RuntimeId = [5], Name = "Search",     ControlType = "Edit",      AriaProperties = "required=true", IsFocusable = true };

        nav.AddChild(edit);
        root.AddChild(h1);
        root.AddChild(link);
        root.AddChild(nav);

        return new VBufferBuilder().Build(root);
    }

    private static readonly IncrementalUpdater Updater = new();

    // -----------------------------------------------------------------------
    // Unknown RuntimeId — returns original document unchanged
    // -----------------------------------------------------------------------

    [Fact]
    public void ApplyUpdate_UnknownRuntimeId_ReturnsOriginalDocument()
    {
        var doc    = BuildBaseDocument();
        var result = Updater.ApplyUpdate(doc, [999], null);
        Assert.Same(doc, result);
    }

    // -----------------------------------------------------------------------
    // Deletion — pass null newSubtreeRoot
    // -----------------------------------------------------------------------

    [Fact]
    public void ApplyUpdate_DeleteLeafNode_RemovesNodeAndText()
    {
        var doc = BuildBaseDocument();
        // Delete the H1 "Welcome" node
        var result = Updater.ApplyUpdate(doc, [2], null);

        Assert.DoesNotContain(result.AllNodes, n => n.Name == "Welcome");
        Assert.DoesNotContain("Welcome", result.FlatText);
    }

    [Fact]
    public void ApplyUpdate_DeleteLeafNode_NodeCountDecreases()
    {
        var doc    = BuildBaseDocument();
        int before = doc.AllNodes.Count;
        var result = Updater.ApplyUpdate(doc, [2], null);
        Assert.Equal(before - 1, result.AllNodes.Count);
    }

    [Fact]
    public void ApplyUpdate_DeleteSubtree_RemovesAllSubtreeNodes()
    {
        var doc = BuildBaseDocument();
        // Delete the Nav group (and its child Edit)
        var result = Updater.ApplyUpdate(doc, [4], null);

        Assert.DoesNotContain(result.AllNodes, n => n.Name == "Nav");
        Assert.DoesNotContain(result.AllNodes, n => n.Name == "Search");
        Assert.DoesNotContain("Search", result.FlatText);
    }

    [Fact]
    public void ApplyUpdate_DeleteSubtree_NodeCountReducedBySubtreeSize()
    {
        var doc    = BuildBaseDocument();
        int before = doc.AllNodes.Count;
        var result = Updater.ApplyUpdate(doc, [4], null);
        // Nav + Edit = 2 nodes removed
        Assert.Equal(before - 2, result.AllNodes.Count);
    }

    // -----------------------------------------------------------------------
    // Replacement — swap a leaf node
    // -----------------------------------------------------------------------

    [Fact]
    public void ApplyUpdate_ReplaceLeafNode_NewNodeAppearsInDocument()
    {
        var doc = BuildBaseDocument();

        var replacement = new MockElement
        {
            RuntimeId   = [2],
            Name        = "Updated Heading",
            ControlType = "Text",
            AriaRole    = "heading",
            AriaProperties = "level=2",
        };

        var result = Updater.ApplyUpdate(doc, [2], replacement);

        Assert.Contains(result.AllNodes, n => n.Name == "Updated Heading");
        Assert.DoesNotContain(result.AllNodes, n => n.Name == "Welcome");
    }

    [Fact]
    public void ApplyUpdate_ReplaceLeafNode_HeadingLevelUpdated()
    {
        var doc = BuildBaseDocument();

        var replacement = new MockElement
        {
            RuntimeId      = [2],
            Name           = "Updated Heading",
            ControlType    = "Text",
            AriaRole       = "heading",
            AriaProperties = "level=2",
        };

        var result  = Updater.ApplyUpdate(doc, [2], replacement);
        var newNode = result.AllNodes.First(n => n.Name == "Updated Heading");
        Assert.Equal(2, newNode.HeadingLevel);
    }

    [Fact]
    public void ApplyUpdate_ReplaceLeafNode_FlatTextUpdated()
    {
        var doc = BuildBaseDocument();

        var replacement = new MockElement
        {
            RuntimeId   = [2],
            Name        = "Updated Heading",
            ControlType = "Text",
            AriaRole    = "heading",
            AriaProperties = "level=1",
        };

        var result = Updater.ApplyUpdate(doc, [2], replacement);

        Assert.Contains("Updated Heading", result.FlatText);
        Assert.DoesNotContain("Welcome",   result.FlatText);
    }

    [Fact]
    public void ApplyUpdate_ReplaceLeafNode_NodeCountUnchanged()
    {
        var doc    = BuildBaseDocument();
        int before = doc.AllNodes.Count;

        var replacement = new MockElement
        {
            RuntimeId      = [2],
            Name           = "Updated Heading",
            ControlType    = "Text",
            AriaRole       = "heading",
            AriaProperties = "level=1",
        };

        var result = Updater.ApplyUpdate(doc, [2], replacement);
        Assert.Equal(before, result.AllNodes.Count);
    }

    // -----------------------------------------------------------------------
    // Replacement — swap a subtree with a larger one
    // -----------------------------------------------------------------------

    [Fact]
    public void ApplyUpdate_ReplaceSubtreeWithLarger_NodeCountIncreases()
    {
        var doc    = BuildBaseDocument();
        int before = doc.AllNodes.Count;

        // Replace "Click here" (single link) with a Group containing two links
        var newGroup  = new MockElement { RuntimeId = [3], Name = "Links",     ControlType = "Group" };
        var newLink1  = new MockElement { RuntimeId = [31], Name = "Home",     ControlType = "Hyperlink", AriaRole = "link" };
        var newLink2  = new MockElement { RuntimeId = [32], Name = "About",    ControlType = "Hyperlink", AriaRole = "link" };
        newGroup.AddChild(newLink1);
        newGroup.AddChild(newLink2);

        var result = Updater.ApplyUpdate(doc, [3], newGroup);

        // Was 1 node (link), now 3 (group + 2 links) → +2
        Assert.Equal(before + 2, result.AllNodes.Count);
        Assert.Contains(result.AllNodes, n => n.Name == "Home");
        Assert.Contains(result.AllNodes, n => n.Name == "About");
    }

    // -----------------------------------------------------------------------
    // Text offset integrity after splice
    // -----------------------------------------------------------------------

    [Fact]
    public void ApplyUpdate_TextRangesAreConsistentAfterUpdate()
    {
        var doc = BuildBaseDocument();

        var replacement = new MockElement
        {
            RuntimeId      = [2],
            Name           = "A much longer heading text that shifts offsets",
            ControlType    = "Text",
            AriaRole       = "heading",
            AriaProperties = "level=1",
        };

        var result = Updater.ApplyUpdate(doc, [2], replacement);

        foreach (var node in result.AllNodes)
        {
            Assert.True(node.TextRange.Start >= 0,
                $"Node '{node.Name}' has negative Start ({node.TextRange.Start})");
            Assert.True(node.TextRange.End >= node.TextRange.Start,
                $"Node '{node.Name}' End < Start");
            Assert.True(node.TextRange.End <= result.FlatText.Length,
                $"Node '{node.Name}' End ({node.TextRange.End}) exceeds FlatText length ({result.FlatText.Length})");

            if (node.HasText)
            {
                var text = result.FlatText[node.TextRange.Start..node.TextRange.End];
                Assert.Contains(node.Name, text);
            }
        }
    }

    [Fact]
    public void ApplyUpdate_NodesAfterSplicedRegion_TextRangesShiftedCorrectly()
    {
        var doc = BuildBaseDocument();

        // Find "Search" node's text range before update
        var searchBefore = doc.AllNodes.First(n => n.Name == "Search");
        int searchStartBefore = searchBefore.TextRange.Start;

        // Replace H1 "Welcome\n" (8+1=9 chars) with "Hi\n" (3 chars) → delta = -6
        var replacement = new MockElement
        {
            RuntimeId      = [2],
            Name           = "Hi",
            ControlType    = "Text",
            AriaRole       = "heading",
            AriaProperties = "level=1",
        };

        var result      = Updater.ApplyUpdate(doc, [2], replacement);
        var searchAfter = result.AllNodes.First(n => n.Name == "Search");

        // "Welcome\n" is 8 chars, "Hi\n" is 3 chars → delta = -5
        int expectedDelta = "Hi\n".Length - "Welcome\n".Length;
        Assert.Equal(searchStartBefore + expectedDelta, searchAfter.TextRange.Start);
    }

    // -----------------------------------------------------------------------
    // Document-order linked list integrity
    // -----------------------------------------------------------------------

    [Fact]
    public void ApplyUpdate_DocumentOrderLinkedList_IsCorrectAfterUpdate()
    {
        var doc = BuildBaseDocument();

        var replacement = new MockElement
        {
            RuntimeId      = [2],
            Name           = "New Heading",
            ControlType    = "Text",
            AriaRole       = "heading",
            AriaProperties = "level=1",
        };

        var result = Updater.ApplyUpdate(doc, [2], replacement);
        var nodes  = result.AllNodes;

        Assert.Null(nodes[0].PrevInOrder);
        Assert.Null(nodes[^1].NextInOrder);

        for (int i = 0; i < nodes.Count - 1; i++)
            Assert.Equal(nodes[i + 1], nodes[i].NextInOrder);

        for (int i = 1; i < nodes.Count; i++)
            Assert.Equal(nodes[i - 1], nodes[i].PrevInOrder);
    }

    // -----------------------------------------------------------------------
    // Pre-built indices are updated
    // -----------------------------------------------------------------------

    [Fact]
    public void ApplyUpdate_IndicesAreRebuiltAfterUpdate()
    {
        var doc = BuildBaseDocument();

        // Delete the link node
        var result = Updater.ApplyUpdate(doc, [3], null);

        Assert.Empty(result.Links);
    }

    [Fact]
    public void ApplyUpdate_HeadingIndexReflectsNewHeadingLevel()
    {
        var doc = BuildBaseDocument();

        var replacement = new MockElement
        {
            RuntimeId      = [2],
            Name           = "Section",
            ControlType    = "Text",
            AriaRole       = "heading",
            AriaProperties = "level=3",
        };

        var result = Updater.ApplyUpdate(doc, [2], replacement);

        Assert.Single(result.Headings);
        Assert.Equal(3, result.Headings[0].HeadingLevel);
    }

    // -----------------------------------------------------------------------
    // FindByRuntimeId after update
    // -----------------------------------------------------------------------

    [Fact]
    public void ApplyUpdate_FindByRuntimeId_FindsReplacedNode()
    {
        var doc = BuildBaseDocument();

        var replacement = new MockElement
        {
            RuntimeId      = [2],
            Name           = "New Title",
            ControlType    = "Text",
            AriaRole       = "heading",
            AriaProperties = "level=1",
        };

        var result   = Updater.ApplyUpdate(doc, [2], replacement);
        var found    = result.FindByRuntimeId([2]);

        Assert.NotNull(found);
        Assert.Equal("New Title", found!.Name);
    }

    [Fact]
    public void ApplyUpdate_FindByRuntimeId_DeletedNodeNotFound()
    {
        var doc    = BuildBaseDocument();
        var result = Updater.ApplyUpdate(doc, [3], null);

        Assert.Null(result.FindByRuntimeId([3]));
    }

    // -----------------------------------------------------------------------
    // Tree integrity and snapshot immutability
    // -----------------------------------------------------------------------

    [Fact]
    public void ApplyUpdate_ReplaceChild_ParentListsNewRootExactlyOnce()
    {
        var doc = BuildBaseDocument();
        var newEdit = new MockElement { RuntimeId = [5], Name = "Find", ControlType = "Edit" };

        var result = Updater.ApplyUpdate(doc, [5], newEdit);

        var nav = result.FindByRuntimeId([4])!;
        var child = Assert.Single(nav.Children);
        Assert.Equal("Find", child.Name);
        Assert.Same(nav, child.Parent);
    }

    [Fact]
    public void ApplyUpdate_IdsAreUniqueAndInDocumentOrder()
    {
        var doc = BuildBaseDocument();
        var newNav = new MockElement { RuntimeId = [4], Name = "Nav", ControlType = "Group", AriaRole = "navigation" };
        newNav.AddChild(new MockElement { RuntimeId = [6], Name = "Home", ControlType = "Hyperlink" });
        newNav.AddChild(new MockElement { RuntimeId = [7], Name = "About", ControlType = "Hyperlink" });

        var result = Updater.ApplyUpdate(doc, [4], newNav);

        for (int i = 0; i < result.AllNodes.Count; i++)
            Assert.Equal(i, result.AllNodes[i].Id);
    }

    [Fact]
    public void ApplyUpdate_DoesNotModifyOriginalDocument()
    {
        var doc = BuildBaseDocument();
        var originalText = doc.FlatText;
        var originalRanges = doc.AllNodes.Select(n => n.TextRange).ToList();
        var originalNavChildren = doc.FindByRuntimeId([4])!.Children.ToList();

        var longer = new MockElement { RuntimeId = [2], Name = "A much longer welcome heading", ControlType = "Text", AriaRole = "heading", AriaProperties = "level=1" };
        Updater.ApplyUpdate(doc, [2], longer);

        Assert.Equal(originalText, doc.FlatText);
        Assert.Equal(originalRanges, doc.AllNodes.Select(n => n.TextRange).ToList());
        Assert.Equal(originalNavChildren, doc.FindByRuntimeId([4])!.Children.ToList());
    }

    [Fact]
    public void ApplyUpdate_TreeLinksMatchDocumentOrder()
    {
        var doc = BuildBaseDocument();
        var newLink = new MockElement { RuntimeId = [3], Name = "Go", ControlType = "Hyperlink" };

        var result = Updater.ApplyUpdate(doc, [3], newLink);

        Assert.Equal(3, result.Root.Children.Count); // H1, link, nav
        Assert.All(result.AllNodes, n =>
        {
            if (n.Parent is not null) Assert.Contains(n, n.Parent.Children);
            Assert.Contains(n, result.AllNodes);
        });
        for (int i = 1; i < result.AllNodes.Count; i++)
        {
            Assert.Same(result.AllNodes[i - 1], result.AllNodes[i].PrevInOrder);
            Assert.Same(result.AllNodes[i], result.AllNodes[i - 1].NextInOrder);
        }
    }

    [Fact]
    public void ApplyUpdate_TextFlipUnderNamedAncestor_ReportsAncestorForRecapture()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        var button = new MockElement { RuntimeId = [2], Name = "Submit", ControlType = "Button" };
        button.AddChild(new MockElement { RuntimeId = [3], Name = "" });
        root.AddChild(button);
        var doc = new VBufferBuilder().Build(root);

        Updater.ApplyUpdate(doc, [3], new MockElement { RuntimeId = [3], Name = "Send" }, out var hint);

        Assert.Equal([2], hint);
    }

    [Fact]
    public void ApplyUpdate_TextChangeWithoutFlip_NeedsNoRecapture()
    {
        var doc = BuildBaseDocument();

        Updater.ApplyUpdate(doc, [2], new MockElement { RuntimeId = [2], Name = "Hello", AriaRole = "heading" }, out var hint);

        Assert.Null(hint);
    }
}
