using Moq;
using Vox.Core.Audio;
using Vox.Core.Buffer;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class QuickNavHandlerTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static (QuickNavHandler handler, Mock<IAudioCuePlayer> mockAudio)
        MakeHandler(VBufferDocument? doc = null, bool wrap = true)
    {
        var mock = new Mock<IAudioCuePlayer>();
        mock.SetupGet(p => p.IsEnabled).Returns(true);
        var handler = new QuickNavHandler(mock.Object) { WrapEnabled = wrap };
        handler.SetDocument(doc);
        return (handler, mock);
    }

    /// <summary>
    /// Builds a simple VBufferDocument with the supplied nodes.
    /// The first node is used as the root Document node; remaining nodes are children.
    /// </summary>
    private static VBufferDocument BuildDoc(IReadOnlyList<VBufferNode> nodes)
    {
        // Assign sequential document-order IDs
        for (int i = 0; i < nodes.Count; i++)
            nodes[i].GetType().GetProperty(nameof(VBufferNode.Id))!.SetValue(nodes[i], i);

        var root = new VBufferNode { Id = -1, UIARuntimeId = [0], ControlType = "Document", Name = "doc" };
        var allNodes = new List<VBufferNode> { root };
        allNodes.AddRange(nodes);

        // Build flat text from node names
        var flatParts = new List<string>();
        int pos = 0;
        foreach (var node in nodes)
        {
            int start = pos;
            int end = pos + node.Name.Length;
            node.TextRange = (start, end);
            flatParts.Add(node.Name);
            pos = end + 1; // +1 for \n separator
        }
        string flatText = string.Join("\n", flatParts);
        root.TextRange = (0, flatText.Length);

        return new VBufferDocument(flatText, root, allNodes);
    }

    private static VBufferNode MakeHeading(int id, int level, string name) =>
        new() { Id = id, UIARuntimeId = [id], Name = name, ControlType = "Heading", HeadingLevel = level, AriaRole = "heading" };

    private static VBufferNode MakeLink(int id, string name) =>
        new() { Id = id, UIARuntimeId = [id], Name = name, ControlType = "Hyperlink", IsLink = true, AriaRole = "link" };

    private static VBufferNode MakeLandmark(int id, string type, string name) =>
        new() { Id = id, UIARuntimeId = [id], Name = name, ControlType = "Group", LandmarkType = type, AriaRole = type };

    private static VBufferNode MakeFormField(int id, string name) =>
        new() { Id = id, UIARuntimeId = [id], Name = name, ControlType = "Edit", IsFocusable = true };

    private static VBufferNode MakeFocusable(int id, string name) =>
        new() { Id = id, UIARuntimeId = [id], Name = name, ControlType = "Button", IsFocusable = true };

    // -------------------------------------------------------------------------
    // No document loaded
    // -------------------------------------------------------------------------

    [Fact]
    public void Handle_NoDocument_ReturnsNull()
    {
        var (handler, _) = MakeHandler(doc: null);
        var result = handler.Handle(NavigationCommand.NextHeading);
        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // NextHeading / PrevHeading
    // -------------------------------------------------------------------------

    [Fact]
    public void NextHeading_FromStart_ReturnsFirstHeading()
    {
        var h1 = MakeHeading(0, 1, "Introduction");
        var h2 = MakeHeading(1, 2, "Details");
        var doc = BuildDoc([h1, h2]);
        var (handler, _) = MakeHandler(doc);

        var result = handler.Handle(NavigationCommand.NextHeading);

        Assert.Same(h1, result);
    }

    [Fact]
    public void NextHeading_AdvancesToNextHeading()
    {
        var h1 = MakeHeading(0, 1, "Intro");
        var h2 = MakeHeading(1, 2, "Details");
        var doc = BuildDoc([h1, h2]);
        var (handler, _) = MakeHandler(doc);

        handler.Handle(NavigationCommand.NextHeading); // -> h1
        var result = handler.Handle(NavigationCommand.NextHeading); // -> h2

        Assert.Same(h2, result);
    }

    [Fact]
    public void PrevHeading_FromSecondHeading_ReturnsFirstHeading()
    {
        var h1 = MakeHeading(0, 1, "Intro");
        var h2 = MakeHeading(1, 2, "Details");
        var doc = BuildDoc([h1, h2]);
        var (handler, _) = MakeHandler(doc);

        handler.Handle(NavigationCommand.NextHeading); // -> h1
        handler.Handle(NavigationCommand.NextHeading); // -> h2
        var result = handler.Handle(NavigationCommand.PrevHeading); // -> h1

        Assert.Same(h1, result);
    }

    [Fact]
    public void NextHeading_AtLastHeading_WithWrap_PlaysWrapAndReturnsFirst()
    {
        var h1 = MakeHeading(0, 1, "Intro");
        var h2 = MakeHeading(1, 2, "Details");
        var doc = BuildDoc([h1, h2]);
        var (handler, mock) = MakeHandler(doc, wrap: true);

        handler.Handle(NavigationCommand.NextHeading); // -> h1
        handler.Handle(NavigationCommand.NextHeading); // -> h2
        var result = handler.Handle(NavigationCommand.NextHeading); // wraps -> h1

        mock.Verify(a => a.Play("wrap"), Times.Once);
        Assert.Same(h1, result);
    }

    [Fact]
    public void NextHeading_AtLastHeading_NoWrap_PlaysBoundaryAndReturnsNull()
    {
        var h1 = MakeHeading(0, 1, "Intro");
        var h2 = MakeHeading(1, 2, "Details");
        var doc = BuildDoc([h1, h2]);
        var (handler, mock) = MakeHandler(doc, wrap: false);

        handler.Handle(NavigationCommand.NextHeading); // -> h1
        handler.Handle(NavigationCommand.NextHeading); // -> h2
        var result = handler.Handle(NavigationCommand.NextHeading); // boundary

        mock.Verify(a => a.Play("boundary"), Times.Once);
        Assert.Null(result);
    }

    [Fact]
    public void PrevHeading_AtFirstHeading_WithWrap_PlaysWrapAndReturnsLast()
    {
        var h1 = MakeHeading(0, 1, "Intro");
        var h2 = MakeHeading(1, 2, "Details");
        var doc = BuildDoc([h1, h2]);
        var (handler, mock) = MakeHandler(doc, wrap: true);

        handler.Handle(NavigationCommand.NextHeading); // -> h1 (now at h1)
        var result = handler.Handle(NavigationCommand.PrevHeading); // wraps -> h2

        mock.Verify(a => a.Play("wrap"), Times.Once);
        Assert.Same(h2, result);
    }

    [Fact]
    public void NextHeading_EmptyList_PlaysBoundaryAndReturnsNull()
    {
        var link = MakeLink(0, "a link");
        var doc = BuildDoc([link]);
        var (handler, mock) = MakeHandler(doc);

        var result = handler.Handle(NavigationCommand.NextHeading);

        mock.Verify(a => a.Play("boundary"), Times.Once);
        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // HeadingLevel1-6 (specific level, forward only)
    // -------------------------------------------------------------------------

    [Fact]
    public void HeadingLevel2_SkipsLevel1_FindsLevel2()
    {
        var h1 = MakeHeading(0, 1, "Top");
        var h2a = MakeHeading(1, 2, "Section A");
        var h2b = MakeHeading(2, 2, "Section B");
        var doc = BuildDoc([h1, h2a, h2b]);
        var (handler, _) = MakeHandler(doc);

        var result = handler.Handle(NavigationCommand.HeadingLevel2);

        Assert.Same(h2a, result);
    }

    [Fact]
    public void HeadingLevel3_NoLevel3Headings_PlaysBoundaryAndReturnsNull()
    {
        var h1 = MakeHeading(0, 1, "Top");
        var h2 = MakeHeading(1, 2, "Section");
        var doc = BuildDoc([h1, h2]);
        var (handler, mock) = MakeHandler(doc);

        var result = handler.Handle(NavigationCommand.HeadingLevel3);

        mock.Verify(a => a.Play("boundary"), Times.Once);
        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // NextLink / PrevLink
    // -------------------------------------------------------------------------

    [Fact]
    public void NextLink_FindsFirstLink()
    {
        var link1 = MakeLink(0, "Google");
        var link2 = MakeLink(1, "GitHub");
        var doc = BuildDoc([link1, link2]);
        var (handler, _) = MakeHandler(doc);

        var result = handler.Handle(NavigationCommand.NextLink);

        Assert.Same(link1, result);
    }

    [Fact]
    public void PrevLink_FromSecondLink_ReturnsFirstLink()
    {
        var link1 = MakeLink(0, "Google");
        var link2 = MakeLink(1, "GitHub");
        var doc = BuildDoc([link1, link2]);
        var (handler, _) = MakeHandler(doc);

        handler.Handle(NavigationCommand.NextLink); // -> link1
        handler.Handle(NavigationCommand.NextLink); // -> link2
        var result = handler.Handle(NavigationCommand.PrevLink);

        Assert.Same(link1, result);
    }

    // -------------------------------------------------------------------------
    // NextLandmark / PrevLandmark
    // -------------------------------------------------------------------------

    [Fact]
    public void NextLandmark_FindsFirstLandmark()
    {
        var nav = MakeLandmark(0, "nav", "Navigation");
        var main = MakeLandmark(1, "main", "Main Content");
        var doc = BuildDoc([nav, main]);
        var (handler, _) = MakeHandler(doc);

        var result = handler.Handle(NavigationCommand.NextLandmark);

        Assert.Same(nav, result);
    }

    [Fact]
    public void PrevLandmark_AtFirstLandmark_WithWrap_WrapsToLast()
    {
        var nav = MakeLandmark(0, "nav", "Navigation");
        var main = MakeLandmark(1, "main", "Main Content");
        var doc = BuildDoc([nav, main]);
        var (handler, mock) = MakeHandler(doc, wrap: true);

        handler.Handle(NavigationCommand.NextLandmark); // -> nav
        var result = handler.Handle(NavigationCommand.PrevLandmark); // wraps -> main

        mock.Verify(a => a.Play("wrap"), Times.Once);
        Assert.Same(main, result);
    }

    // -------------------------------------------------------------------------
    // NextFormField / PrevFormField
    // -------------------------------------------------------------------------

    [Fact]
    public void NextFormField_FindsFirstFormField()
    {
        var field1 = MakeFormField(0, "Name");
        var field2 = MakeFormField(1, "Email");
        var doc = BuildDoc([field1, field2]);
        var (handler, _) = MakeHandler(doc);

        var result = handler.Handle(NavigationCommand.NextFormField);

        Assert.Same(field1, result);
    }

    [Fact]
    public void PrevFormField_FromSecondField_ReturnsFirstField()
    {
        var field1 = MakeFormField(0, "Name");
        var field2 = MakeFormField(1, "Email");
        var doc = BuildDoc([field1, field2]);
        var (handler, _) = MakeHandler(doc);

        handler.Handle(NavigationCommand.NextFormField); // -> field1
        handler.Handle(NavigationCommand.NextFormField); // -> field2
        var result = handler.Handle(NavigationCommand.PrevFormField);

        Assert.Same(field1, result);
    }

    // -------------------------------------------------------------------------
    // NextFocusable / PrevFocusable
    // -------------------------------------------------------------------------

    [Fact]
    public void NextFocusable_FindsFirstFocusableElement()
    {
        var btn1 = MakeFocusable(0, "Submit");
        var btn2 = MakeFocusable(1, "Cancel");
        var doc = BuildDoc([btn1, btn2]);
        var (handler, _) = MakeHandler(doc);

        var result = handler.Handle(NavigationCommand.NextFocusable);

        Assert.NotNull(result);
        Assert.True(result.IsFocusable);
    }

    [Fact]
    public void PrevFocusable_AtFirstElement_WithWrap_WrapsToLast()
    {
        var btn1 = MakeFocusable(0, "Submit");
        var btn2 = MakeFocusable(1, "Cancel");
        var doc = BuildDoc([btn1, btn2]);
        var (handler, mock) = MakeHandler(doc, wrap: true);

        // Set current to first focusable
        handler.Handle(NavigationCommand.NextFocusable); // -> btn1
        var result = handler.Handle(NavigationCommand.PrevFocusable); // wraps -> btn2

        mock.Verify(a => a.Play("wrap"), Times.Once);
        Assert.Same(btn2, result);
    }

    // -------------------------------------------------------------------------
    // NextTable / PrevTable — not indexed, always boundary
    // -------------------------------------------------------------------------

    [Fact]
    public void NextTable_PlaysBoundaryAndReturnsNull()
    {
        var h1 = MakeHeading(0, 1, "Page");
        var doc = BuildDoc([h1]);
        var (handler, mock) = MakeHandler(doc);

        var result = handler.Handle(NavigationCommand.NextTable);

        mock.Verify(a => a.Play("boundary"), Times.Once);
        Assert.Null(result);
    }

    [Fact]
    public void PrevTable_PlaysBoundaryAndReturnsNull()
    {
        var h1 = MakeHeading(0, 1, "Page");
        var doc = BuildDoc([h1]);
        var (handler, mock) = MakeHandler(doc);

        var result = handler.Handle(NavigationCommand.PrevTable);

        mock.Verify(a => a.Play("boundary"), Times.Once);
        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // SetDocument resets CurrentNode
    // -------------------------------------------------------------------------

    [Fact]
    public void SetDocument_ResetsCurrentNode()
    {
        var h1 = MakeHeading(0, 1, "Intro");
        var doc = BuildDoc([h1]);
        var (handler, _) = MakeHandler(doc);

        handler.Handle(NavigationCommand.NextHeading); // -> h1
        Assert.NotNull(handler.CurrentNode);

        handler.SetDocument(null);
        Assert.Null(handler.CurrentNode);
    }

    // -------------------------------------------------------------------------
    // CurrentNode tracks position between different element types
    // -------------------------------------------------------------------------

    [Fact]
    public void Navigation_CurrentNode_UpdatedAfterEachCall()
    {
        var h1 = MakeHeading(0, 1, "Intro");
        var h2 = MakeHeading(1, 2, "Details");
        var doc = BuildDoc([h1, h2]);
        var (handler, _) = MakeHandler(doc);

        handler.Handle(NavigationCommand.NextHeading);
        Assert.Same(h1, handler.CurrentNode);

        handler.Handle(NavigationCommand.NextHeading);
        Assert.Same(h2, handler.CurrentNode);
    }

    // -------------------------------------------------------------------------
    // Unhandled command returns null without playing audio
    // -------------------------------------------------------------------------

    [Fact]
    public void UnhandledCommand_ReturnsNull_NoAudio()
    {
        var h1 = MakeHeading(0, 1, "Intro");
        var doc = BuildDoc([h1]);
        var (handler, mock) = MakeHandler(doc);

        var result = handler.Handle(NavigationCommand.SayAll);

        Assert.Null(result);
        mock.Verify(a => a.Play(It.IsAny<string>()), Times.Never);
    }
}
