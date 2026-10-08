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

    [Fact]
    public void PrevHeading_FromInsideHeadingText_FindsThePreviousHeading()
    {
        var root = new Vox.Core.Tests.Buffer.MockElement { RuntimeId = [1], ControlType = "Document" };
        var h1 = new Vox.Core.Tests.Buffer.MockElement { RuntimeId = [2], Name = "First", AriaRole = "heading", ControlType = "Group" };
        h1.AddChild(new Vox.Core.Tests.Buffer.MockElement { RuntimeId = [3], Name = "First" });
        var h2 = new Vox.Core.Tests.Buffer.MockElement { RuntimeId = [4], Name = "Second", AriaRole = "heading", ControlType = "Group" };
        h2.AddChild(new Vox.Core.Tests.Buffer.MockElement { RuntimeId = [5], Name = "Second" });
        root.AddChild(h1);
        root.AddChild(h2);
        var doc = new VBufferBuilder().Build(root);
        var (handler, _) = MakeHandler(doc, wrap: false);

        handler.CurrentNode = doc.FindByRuntimeId([5]); // the second heading's text

        var result = handler.Handle(NavigationCommand.PrevHeading);

        Assert.Equal([2], result!.UIARuntimeId);
    }

    // -------------------------------------------------------------------------
    // Form controls by kind (B, E, C, X, R)
    // -------------------------------------------------------------------------

    private static VBufferDocument FormDoc() => BuildDoc(
    [
        new() { UIARuntimeId = [1], Name = "Name", ControlType = "Edit" },
        new() { UIARuntimeId = [2], Name = "Country", ControlType = "ComboBox" },
        new() { UIARuntimeId = [3], Name = "Subscribe", ControlType = "CheckBox" },
        new() { UIARuntimeId = [4], Name = "Dark mode", ControlType = "Button", AriaRole = "switch" },
        new() { UIARuntimeId = [5], Name = "Small", ControlType = "RadioButton" },
        new() { UIARuntimeId = [6], Name = "Large", ControlType = "Custom", AriaRole = "radio" },
        new() { UIARuntimeId = [7], Name = "Search", ControlType = "Edit", AriaRole = "searchbox" },
        new() { UIARuntimeId = [8], Name = "Send", ControlType = "Button" },
        new() { UIARuntimeId = [9], Name = "Help", ControlType = "Custom", AriaRole = "button" },
    ]);

    [Theory]
    [InlineData(NavigationCommand.NextEdit, "Name", "Search")]
    [InlineData(NavigationCommand.NextComboBox, "Country", "Country")]
    [InlineData(NavigationCommand.NextCheckBox, "Subscribe", "Dark mode")]
    [InlineData(NavigationCommand.NextRadioButton, "Small", "Large")]
    [InlineData(NavigationCommand.NextButton, "Dark mode", "Send")]
    public void NextOfAKind_FindsEachInTurn(NavigationCommand command, string first, string second)
    {
        var (handler, _) = MakeHandler(FormDoc());

        Assert.Equal(first, handler.Handle(command)?.Name);
        Assert.Equal(second, handler.Handle(command)?.Name);
    }

    [Theory]
    [InlineData(NavigationCommand.PrevButton, "Help")]
    [InlineData(NavigationCommand.PrevEdit, "Search")]
    [InlineData(NavigationCommand.PrevRadioButton, "Large")]
    public void PrevOfAKind_FromTheStart_WrapsToTheLast(NavigationCommand command, string expected)
    {
        var (handler, audio) = MakeHandler(FormDoc());

        Assert.Equal(expected, handler.Handle(command)?.Name);
    }

    [Fact]
    public void NoneOfAKind_PlaysTheBoundary()
    {
        var (handler, audio) = MakeHandler(BuildDoc([new() { UIARuntimeId = [1], Name = "Just text" }]));

        Assert.Null(handler.Handle(NavigationCommand.NextComboBox));
        audio.Verify(a => a.Play("boundary"), Times.Once);
    }

    [Theory]
    [InlineData(NavigationCommand.NextButton)]
    [InlineData(NavigationCommand.PrevCheckBox)]
    [InlineData(NavigationCommand.NextRadioButton)]
    public void FormKindCommands_AreQuickNavCommands(NavigationCommand command)
    {
        Assert.True(QuickNavHandler.IsQuickNavCommand(command));
    }

    // -------------------------------------------------------------------------
    // Page elements by kind (L, I, G, Q, M, S, O)
    // -------------------------------------------------------------------------

    private static VBufferDocument PageDoc() => BuildDoc(
    [
        new() { UIARuntimeId = [1], Name = "Fruit", ControlType = "List" },
        new() { UIARuntimeId = [2], Name = "Apples", ControlType = "ListItem" },
        new() { UIARuntimeId = [3], Name = "Logo", ControlType = "Image" },
        new() { UIARuntimeId = [4], Name = "Pears", ControlType = "ListItem", AriaRole = "listitem" },
        new() { UIARuntimeId = [5], Name = "Sizes", ControlType = "List", AriaRole = "listbox" },
        new() { UIARuntimeId = [6], Name = "Small", ControlType = "ListItem", AriaRole = "option" },
        new() { UIARuntimeId = [7], Name = "Quote", ControlType = "Group", AriaRole = "blockquote" },
        new() { UIARuntimeId = [8], Name = "", ControlType = "Separator" },
        new() { UIARuntimeId = [9], Name = "Ad", ControlType = "Pane", AriaRole = "iframe" },
        new() { UIARuntimeId = [10], Name = "Player", ControlType = "Pane", AriaRole = "application" },
        new() { UIARuntimeId = [11], Name = "Chart", ControlType = "Group", AriaRole = "img" },
        new() { UIARuntimeId = [12], Name = "Steps", ControlType = "Group", AriaRole = "list" },
        new() { UIARuntimeId = [13], Name = "Video", ControlType = "Group", AriaRole = "video" },
        new() { UIARuntimeId = [14], Name = "Rule", ControlType = "Group", AriaRole = "separator" },
        new() { UIARuntimeId = [15], Name = "Said", ControlType = "Group", AriaRole = "blockquote" },
        new() { UIARuntimeId = [16], Name = "Map", ControlType = "Pane", AriaRole = "frame" },
    ]);

    [Theory]
    [InlineData(NavigationCommand.NextList, "Fruit", "Steps")]
    [InlineData(NavigationCommand.NextListItem, "Apples", "Pears")]
    [InlineData(NavigationCommand.NextGraphic, "Logo", "Chart")]
    [InlineData(NavigationCommand.NextBlockQuote, "Quote", "Said")]
    [InlineData(NavigationCommand.NextSeparator, "", "Rule")]
    [InlineData(NavigationCommand.NextFrame, "Ad", "Map")]
    [InlineData(NavigationCommand.NextEmbeddedObject, "Player", "Video")]
    public void NextPageElementOfAKind_FindsEachInTurn(NavigationCommand command, string first, string second)
    {
        var (handler, _) = MakeHandler(PageDoc());

        Assert.Equal(first, handler.Handle(command)?.Name);
        Assert.Equal(second, handler.Handle(command)?.Name);
    }

    [Fact]
    public void NextList_SkipsListBoxes_AndWrapsToTheFirstList()
    {
        var (handler, audio) = MakeHandler(PageDoc());

        handler.Handle(NavigationCommand.NextList);
        handler.Handle(NavigationCommand.NextList);
        var third = handler.Handle(NavigationCommand.NextList);

        Assert.Equal("Fruit", third?.Name);
        audio.Verify(a => a.Play("wrap"), Times.Once);
    }

    [Theory]
    [InlineData(NavigationCommand.PrevList, "Steps")]
    [InlineData(NavigationCommand.PrevListItem, "Pears")]
    [InlineData(NavigationCommand.PrevGraphic, "Chart")]
    [InlineData(NavigationCommand.PrevFrame, "Map")]
    public void PrevPageElementOfAKind_FromTheStart_WrapsToTheLast(NavigationCommand command, string expected)
    {
        var (handler, _) = MakeHandler(PageDoc());

        Assert.Equal(expected, handler.Handle(command)?.Name);
    }

    [Fact]
    public void NoPageElementOfAKind_PlaysTheBoundary()
    {
        var (handler, audio) = MakeHandler(BuildDoc([new() { UIARuntimeId = [1], Name = "Just text" }]));

        Assert.Null(handler.Handle(NavigationCommand.NextBlockQuote));
        audio.Verify(a => a.Play("boundary"), Times.Once);
    }

    [Theory]
    [InlineData(NavigationCommand.NextList, "list")]
    [InlineData(NavigationCommand.PrevListItem, "list item")]
    [InlineData(NavigationCommand.NextGraphic, "graphic")]
    [InlineData(NavigationCommand.PrevBlockQuote, "block quote")]
    [InlineData(NavigationCommand.NextFrame, "frame")]
    [InlineData(NavigationCommand.NextSeparator, "separator")]
    [InlineData(NavigationCommand.PrevEmbeddedObject, "embedded object")]
    public void PageElementCommands_AreQuickNavCommandsWithAKindName(NavigationCommand command, string kind)
    {
        Assert.True(QuickNavHandler.IsQuickNavCommand(command));
        Assert.Equal(kind, QuickNavHandler.ElementKindName(command));
    }

    [Fact]
    public void OtherCommands_HaveNoKindName()
    {
        Assert.Null(QuickNavHandler.ElementKindName(NavigationCommand.NextButton));
        Assert.Null(QuickNavHandler.ElementKindName(NavigationCommand.NextTable));
    }
}
