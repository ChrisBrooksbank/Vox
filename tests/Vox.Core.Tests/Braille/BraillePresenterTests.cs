using Vox.Core.Braille;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Xunit;

namespace Vox.Core.Tests.Braille;

public class BraillePresenterTests
{
    // -------------------------------------------------------------------------
    // Focus
    // -------------------------------------------------------------------------

    private static readonly BrailleObject Window = new() { Name = "Notepad", ControlType = "Window", RuntimeId = [1] };
    private static readonly BrailleObject LayoutGroup = new() { ControlType = "Group", RuntimeId = [2] };
    private static readonly BrailleObject Dialog = new() { Name = "Save as", ControlType = "Window", AriaRole = "dialog", RuntimeId = [3] };
    private static readonly BrailleObject Toolbar = new() { Name = "Actions", ControlType = "ToolBar", RuntimeId = [4] };
    private static readonly BrailleObject OkButton = new() { Name = "OK", ControlType = "Button", RuntimeId = [10] };

    [Fact]
    public void ForFocus_FillDisplay_ShowsContextThenObject_AnchoredOnTheObject()
    {
        var line = new BraillePresenter().ForFocus(OkButton, [Window, LayoutGroup, Dialog], BrailleFocusContext.FillDisplay);

        Assert.Equal(BrailleLineKind.Focus, line.Kind);
        Assert.Equal("Notepad wnd Save as dlg OK btn", line.Text);
        Assert.Equal(line.Text.IndexOf("OK", StringComparison.Ordinal), line.AnchorOffset);
        Assert.Equal(BrailleAnchorMode.FillBeforeAnchor, line.AnchorMode);
        Assert.Null(line.CursorOffset);
        Assert.Equal(line.Text.Length, line.SourceEnd);
    }

    [Fact]
    public void ForFocus_ScrollBackOnly_StartsAtTheObject()
    {
        var line = new BraillePresenter().ForFocus(OkButton, [Window, Dialog], BrailleFocusContext.ScrollBackOnly);

        Assert.Equal("Notepad wnd Save as dlg OK btn", line.Text);
        Assert.Equal(BrailleAnchorMode.AtAnchor, line.AnchorMode);
        Assert.Equal("OK btn", line.Text[line.AnchorOffset..]);
    }

    [Fact]
    public void ForFocus_WithoutAncestors_IsTheObjectAlone()
    {
        var line = new BraillePresenter().ForFocus(OkButton, [], BrailleFocusContext.FillDisplay);

        Assert.Equal("OK btn", line.Text);
        Assert.Equal(0, line.AnchorOffset);
    }

    [Fact]
    public void ForFocus_ChangedContext_ShowsOnlyAncestorsFromTheFirstThatChanged()
    {
        var presenter = new BraillePresenter();
        var cancel = new BrailleObject { Name = "Cancel", ControlType = "Button", RuntimeId = [11] };

        var first = presenter.ForFocus(OkButton, [Window, Dialog], BrailleFocusContext.ChangedContext);
        var second = presenter.ForFocus(cancel, [Window, Dialog], BrailleFocusContext.ChangedContext);
        var third = presenter.ForFocus(OkButton, [Window, Dialog, Toolbar], BrailleFocusContext.ChangedContext);
        var fourth = presenter.ForFocus(cancel, [Window, Toolbar], BrailleFocusContext.ChangedContext);

        Assert.Equal("Notepad wnd Save as dlg OK btn", first.Text);
        Assert.Equal("Cancel btn", second.Text);
        Assert.Equal("Actions tlbar OK btn", third.Text);
        Assert.Equal("Actions tlbar Cancel btn", fourth.Text);
        Assert.Equal(BrailleAnchorMode.FillBeforeAnchor, fourth.AnchorMode);
    }

    [Fact]
    public void ForFocus_ResetContext_ShowsTheWholeContextAgain()
    {
        var presenter = new BraillePresenter();
        presenter.ForFocus(OkButton, [Window, Dialog], BrailleFocusContext.ChangedContext);

        presenter.ResetContext();
        var line = presenter.ForFocus(OkButton, [Window, Dialog], BrailleFocusContext.ChangedContext);

        Assert.Equal("Notepad wnd Save as dlg OK btn", line.Text);
    }

    // -------------------------------------------------------------------------
    // Edit line
    // -------------------------------------------------------------------------

    [Fact]
    public void ForEditLine_KeepsCaretAndSelection()
    {
        var line = BraillePresenter.ForEditLine("hello world", 3, (1, 4));

        Assert.Equal(BrailleLineKind.EditLine, line.Kind);
        Assert.Equal("hello world", line.Text);
        Assert.Equal(3, line.CursorOffset);
        Assert.Equal((1, 4), line.Selection);
        Assert.Null(line.SourceOffsets!);
        Assert.Equal(11, line.SourceEnd);
        Assert.Equal(4, line.SourceOffsetAt(4));
    }

    [Fact]
    public void ForEditLine_CutsAtTheLineBreak_AndClipsCaretAndSelection()
    {
        var line = BraillePresenter.ForEditLine("hello\r\nworld", 9, (2, 10));

        Assert.Equal("hello", line.Text);
        Assert.Equal(5, line.CursorOffset);
        Assert.Equal((2, 5), line.Selection);
        Assert.Equal(5, line.SourceEnd);
    }

    [Fact]
    public void ForEditLine_SelectionOffTheLine_IsNone()
    {
        Assert.Null(BraillePresenter.ForEditLine("hello", 0, (7, 9)).Selection);
        Assert.Null(BraillePresenter.ForEditLine("hello", 0, (2, 2)).Selection);
        Assert.Null(BraillePresenter.ForEditLine("hello", null, null).CursorOffset);
    }

    [Fact]
    public void ForEditLine_TabsBecomeSpaces()
    {
        Assert.Equal("a b", BraillePresenter.ForEditLine("a\tb", 0, null).Text);
    }

    [Fact]
    public void ForReviewLine_IsAReviewLineWithACursor()
    {
        var line = BraillePresenter.ForReviewLine("first\nsecond", 2);

        Assert.Equal(BrailleLineKind.Review, line.Kind);
        Assert.Equal("first", line.Text);
        Assert.Equal(2, line.CursorOffset);
        Assert.Null(line.Selection);
    }

    // -------------------------------------------------------------------------
    // Browse line
    // -------------------------------------------------------------------------

    // FlatText: "Go Home now\nSubscribe\nIntro\n"
    //            0123456789012345678901234567
    //  Text "Go " (0,3), link "Home" (3,7), text " now" (7,11)
    //  check box "Subscribe" (12,21), heading 2 "Intro" (22,27)
    private const string PageText = "Go Home now\nSubscribe\nIntro\n";

    private static VBufferDocument BuildPage(bool visited = false)
    {
        var id = 0;
        VBufferNode Node(string name, string controlType, (int, int) range)
        {
            var node = new VBufferNode { Id = id, UIARuntimeId = [id], Name = name, ControlType = controlType, TextRange = range };
            id++;
            return node;
        }

        var root = Node("Page", "Document", (0, PageText.Length));
        var go = Node("Go ", "Text", (0, 3));
        var link = new VBufferNode
        {
            Id = 100, UIARuntimeId = [100], Name = "Home", ControlType = "Hyperlink", IsLink = true, IsVisited = visited,
            IsFocusable = true, TextRange = (3, 7),
        };
        var now = Node(" now", "Text", (7, 11));
        var check = new VBufferNode
        {
            Id = 101, UIARuntimeId = [101], Name = "Subscribe", ControlType = "CheckBox", ToggleState = 0,
            IsFocusable = true, TextRange = (12, 21),
        };
        var heading = new VBufferNode { Id = 102, UIARuntimeId = [102], Name = "Intro", ControlType = "Text", HeadingLevel = 2, TextRange = (22, 27) };

        VBufferNode[] children = [go, link, now, check, heading];
        foreach (var child in children)
        {
            child.Parent = root;
            root.Children.Add(child);
        }
        return new VBufferDocument(PageText, root, [root, .. children]);
    }

    [Fact]
    public void ForBrowseLine_AddsRoleLabelAfterTheLink()
    {
        var line = new BraillePresenter().ForBrowseLine(BuildPage(), 0, 11, 0);

        Assert.Equal(BrailleLineKind.BrowseLine, line.Kind);
        Assert.Equal("Go Home lnk now", line.Text);
        Assert.Equal(0, line.SourceStart);
        Assert.Equal(11, line.SourceEnd);
    }

    [Fact]
    public void ForBrowseLine_VisitedLink_ShowsV()
    {
        var line = new BraillePresenter().ForBrowseLine(BuildPage(visited: true), 0, 11, 0);

        Assert.Equal("Go Home lnk v now", line.Text);
    }

    [Fact]
    public void ForBrowseLine_MapsTextAndLabelCharactersToTheSource()
    {
        var line = new BraillePresenter().ForBrowseLine(BuildPage(), 0, 11, 0);

        // "Go Home" is the page's own text; " lnk" maps to the link's start; " now" follows on
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 3, 3, 3, 3, 7, 8, 9, 10], line.SourceOffsets!);
        Assert.Equal(3, line.SourceOffsetAt(line.Text.IndexOf("lnk", StringComparison.Ordinal)));
        Assert.Equal(11, line.SourceOffsetAt(line.Text.Length));
        Assert.Equal(11, line.TextOffsetOf(7));
        Assert.Equal(line.Text.Length, line.TextOffsetOf(11));
        Assert.Null(line.TextOffsetOf(15));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(7, 11)]
    [InlineData(9, 13)]
    [InlineData(11, 15)]
    public void ForBrowseLine_MapsTheCursor(int cursor, int expected)
    {
        var line = new BraillePresenter().ForBrowseLine(BuildPage(), 0, 11, cursor);

        Assert.Equal(expected, line.CursorOffset);
    }

    [Fact]
    public void ForBrowseLine_CursorOnAnotherLine_IsNone()
    {
        var line = new BraillePresenter().ForBrowseLine(BuildPage(), 0, 11, 15);

        Assert.Null(line.CursorOffset);
    }

    [Fact]
    public void ForBrowseLine_MapsTheSelection()
    {
        var presenter = new BraillePresenter();

        // "Home" selected: its label goes with it
        Assert.Equal((3, 11), presenter.ForBrowseLine(BuildPage(), 0, 11, 7, (3, 7)).Selection);
        // From the line's start into "now"
        Assert.Equal((0, 13), presenter.ForBrowseLine(BuildPage(), 0, 11, 9, (0, 9)).Selection);
        // Into the next line: clipped to the line's end
        Assert.Equal((12, 15), presenter.ForBrowseLine(BuildPage(), 0, 11, 8, (8, 20)).Selection);
        // On another line only
        Assert.Null(presenter.ForBrowseLine(BuildPage(), 0, 11, 0, (12, 20)).Selection);
    }

    [Fact]
    public void ForBrowseLine_CheckBoxShowsRoleAndState()
    {
        var line = new BraillePresenter().ForBrowseLine(BuildPage(), 12, 21, 12);

        Assert.Equal("Subscribe chk ( )", line.Text);
        Assert.Equal(0, line.CursorOffset);
        Assert.Equal(12, line.SourceOffsetAt(line.Text.Length - 1));
        Assert.Equal(21, line.SourceEnd);
    }

    [Fact]
    public void ForBrowseLine_HeadingShowsItsLevel()
    {
        var line = new BraillePresenter().ForBrowseLine(BuildPage(), 22, 27, 22);

        Assert.Equal("Intro h2", line.Text);
    }

    [Fact]
    public void ForBrowseLine_LineBreaksBecomeSpaces()
    {
        var line = new BraillePresenter().ForBrowseLine(BuildPage(), 22, 28, 22);

        Assert.Equal("Intro h2 ", line.Text);
        Assert.DoesNotContain('\n', line.Text);
    }

    [Fact]
    public void ForBrowseLine_ElementStartingBeforeTheLine_IsLabelledAtItsEnd()
    {
        // A link starting before the line and ending on it is labelled at its end, mapped to the line's start
        var line = new BraillePresenter().ForBrowseLine(BuildPage(), 4, 7, 4);

        Assert.Equal("ome lnk", line.Text);
        Assert.Equal(4, line.SourceOffsetAt(4));
    }

    [Fact]
    public void ForBrowseLine_ElementSpanningPastBothEnds_IsNotLabelled()
    {
        var line = new BraillePresenter().ForBrowseLine(BuildPage(), 4, 6, 4);

        Assert.Equal("om", line.Text);
    }

    [Fact]
    public void Annotation_IsRoleAndStatesWithoutNameOrValue()
    {
        var presenter = new BraillePresenter();
        var node = new VBufferNode
        {
            Name = "Email", ControlType = "Edit", Value = "me@example.com", IsRequired = true, PositionInSet = 1, SizeOfSet = 3,
        };

        Assert.Equal("edt req", presenter.Annotation(node));
    }

    [Fact]
    public void CustomLabels_AreUsed()
    {
        var presenter = new BraillePresenter(BrailleLabels.FromJson("""{"roles": {"Hyperlink": "LINK"}}"""));

        Assert.Equal("Go Home LINK now", presenter.ForBrowseLine(BuildPage(), 0, 11, 0).Text);
    }
}
