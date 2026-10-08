using Moq;
using Vox.Core.Audio;
using Vox.Core.Buffer;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class BrowseSelectionTests
{
    private readonly BrowseSelection _selection = new();

    private static VBufferDocument BuildDoc(string flatText)
    {
        var root = new VBufferNode
        {
            Id = 0,
            UIARuntimeId = [0],
            ControlType = "Document",
            TextRange = (0, flatText.Length),
        };
        var text = new VBufferNode
        {
            Id = 1,
            UIARuntimeId = [1],
            Name = flatText,
            ControlType = "Text",
            TextRange = (0, flatText.Length),
            Parent = root,
        };
        root.Children.Add(text);
        return new VBufferDocument(flatText, root, [root, text]);
    }

    private static VBufferCursor Cursor(string flatText, int offset = 0)
    {
        var cursor = new VBufferCursor(BuildDoc(flatText), Mock.Of<IAudioCuePlayer>());
        cursor.MoveTo(offset);
        return cursor;
    }

    [Fact]
    public void ShiftRight_SelectsTheCharacterUnderTheCursor_AndMovesOn()
    {
        var cursor = Cursor("Hello world");

        var change = _selection.Extend(cursor, NavigationCommand.SelectNextChar);

        Assert.Equal(new SelectionChange("H", true), change);
        Assert.Equal(1, cursor.TextOffset);
        Assert.Equal("H", _selection.TextFor(cursor));
    }

    [Fact]
    public void ShiftLeft_AfterShiftRight_UnselectsIt()
    {
        var cursor = Cursor("Hello world");
        _selection.Extend(cursor, NavigationCommand.SelectNextChar);
        _selection.Extend(cursor, NavigationCommand.SelectNextChar);

        var change = _selection.Extend(cursor, NavigationCommand.SelectPrevChar);

        Assert.Equal(new SelectionChange("e", false), change);
        Assert.Equal("H", _selection.TextFor(cursor));
    }

    [Fact]
    public void CtrlShiftRight_SelectsAWordWithItsSpace_ThenTheRest()
    {
        var cursor = Cursor("Hello big world");

        Assert.Equal(new SelectionChange("Hello ", true), _selection.Extend(cursor, NavigationCommand.SelectNextWord));
        Assert.Equal(new SelectionChange("big ", true), _selection.Extend(cursor, NavigationCommand.SelectNextWord));
        Assert.Equal(new SelectionChange("world", true), _selection.Extend(cursor, NavigationCommand.SelectNextWord));
        Assert.Null(_selection.Extend(cursor, NavigationCommand.SelectNextWord));
        Assert.Equal("Hello big world", _selection.TextFor(cursor));
    }

    [Fact]
    public void CtrlShiftLeft_SelectsBackwardsByWord()
    {
        var cursor = Cursor("Hello big world", 10);

        Assert.Equal(new SelectionChange("big ", true), _selection.Extend(cursor, NavigationCommand.SelectPrevWord));
        Assert.Equal(6, cursor.TextOffset);
        Assert.Equal("big ", _selection.TextFor(cursor));
    }

    [Fact]
    public void ShiftDown_SelectsToTheSameColumnOfTheNextLine_AndShiftUpUndoesIt()
    {
        var cursor = Cursor("First line\nSecond line\n", 2);

        var down = _selection.Extend(cursor, NavigationCommand.SelectNextLine);
        Assert.Equal(new SelectionChange("rst line\nSe", true), down);
        Assert.Equal(13, cursor.TextOffset);

        var up = _selection.Extend(cursor, NavigationCommand.SelectPrevLine);
        Assert.Equal(new SelectionChange("rst line\nSe", false), up);
        Assert.Equal("", _selection.TextFor(cursor));
    }

    [Fact]
    public void ShiftDown_OnTheLastLine_SelectsToTheEnd_IncludingTheLastCharacter()
    {
        var cursor = Cursor("One\nTwo", 4);

        Assert.Equal(new SelectionChange("Two", true), _selection.Extend(cursor, NavigationCommand.SelectNextLine));
        Assert.Equal(6, cursor.TextOffset); // the cursor stays on the last character
        Assert.Equal("Two", _selection.TextFor(cursor));
        Assert.Null(_selection.Extend(cursor, NavigationCommand.SelectNextLine));
    }

    [Fact]
    public void ShiftUp_OnTheFirstLine_SelectsToTheStart()
    {
        var cursor = Cursor("One\nTwo", 2);

        Assert.Equal(new SelectionChange("On", true), _selection.Extend(cursor, NavigationCommand.SelectPrevLine));
        Assert.Null(_selection.Extend(cursor, NavigationCommand.SelectPrevLine));
    }

    [Fact]
    public void ShiftHomeAndEnd_SelectToTheLineEdges_WithoutTheLineBreak()
    {
        var cursor = Cursor("One two\nThree", 4);

        Assert.Equal(new SelectionChange("two", true), _selection.Extend(cursor, NavigationCommand.SelectToEndOfLine));
        // Back past the anchor: the new selection is said
        Assert.Equal(new SelectionChange("One ", true), _selection.Extend(cursor, NavigationCommand.SelectToStartOfLine));
        Assert.Equal("One ", _selection.TextFor(cursor));
        Assert.Null(_selection.Extend(cursor, NavigationCommand.SelectToStartOfLine));
    }

    [Fact]
    public void CtrlShiftHomeEnd_AndSelectAll_SelectToTheDocumentEdges()
    {
        var cursor = Cursor("One\nTwo", 4);

        Assert.Equal(new SelectionChange("Two", true), _selection.Extend(cursor, NavigationCommand.SelectToBottom));
        _selection.Clear();
        cursor.MoveTo(4);
        Assert.Equal(new SelectionChange("One\n", true), _selection.Extend(cursor, NavigationCommand.SelectToTop));

        _selection.Extend(cursor, NavigationCommand.SelectAll);
        Assert.Equal("One\nTwo", _selection.TextFor(cursor));
    }

    [Fact]
    public void MovingTheCursorAnotherWay_EndsTheSelection()
    {
        var cursor = Cursor("Hello world");
        _selection.Extend(cursor, NavigationCommand.SelectNextChar);

        cursor.NextChar();

        Assert.Equal("", _selection.TextFor(cursor));
        // Selecting again starts afresh at the cursor
        Assert.Equal(new SelectionChange("l", true), _selection.Extend(cursor, NavigationCommand.SelectNextChar));
    }

    [Fact]
    public void ANewDocument_EndsTheSelection()
    {
        var cursor = Cursor("Hello world");
        _selection.Extend(cursor, NavigationCommand.SelectNextChar);

        cursor.SetDocument(BuildDoc("Hello world"), 1);

        Assert.Equal("", _selection.TextFor(cursor));
    }

    [Fact]
    public void Rebase_KeepsASelectionBeforeTheChange_ShiftsOneAfterIt_AndEndsOneItOverlaps()
    {
        var cursor = Cursor("Hello world");
        _selection.Extend(cursor, NavigationCommand.SelectNextWord); // "Hello "

        // Text replaced after the selection
        var updated = BuildDoc("Hello there world");
        cursor.SetDocument(updated, cursor.TextOffset);
        _selection.Rebase(updated, 6, 6, 6, cursor.TextOffset);
        Assert.Equal("Hello ", _selection.TextFor(cursor));

        // Text inserted before it
        var shifted = BuildDoc(">> Hello there world");
        cursor.SetDocument(shifted, cursor.TextOffset + 3);
        _selection.Rebase(shifted, 0, 0, 3, cursor.TextOffset);
        Assert.Equal("Hello ", _selection.TextFor(cursor));

        // The selected text itself replaced
        var replaced = BuildDoc(">> Bye there world");
        cursor.SetDocument(replaced, cursor.TextOffset);
        _selection.Rebase(replaced, 3, 8, -2, cursor.TextOffset);
        Assert.Equal("", _selection.TextFor(cursor));
    }

    [Fact]
    public void SelectFromMark_SelectsFromTheMarkToTheCursor_BothIncluded_WithoutMovingIt()
    {
        var cursor = Cursor("Hello world");
        Assert.False(_selection.SelectFromMark(cursor)); // no mark yet

        cursor.MoveTo(1);
        _selection.SetMark(cursor);
        cursor.MoveTo(4);

        Assert.True(_selection.SelectFromMark(cursor));
        Assert.Equal("ello", _selection.TextFor(cursor));
        Assert.Equal(4, cursor.TextOffset);
        Assert.Equal(1, _selection.Anchor);
    }

    [Fact]
    public void SelectFromMark_BeforeTheMark_SelectsBackwards_AndShiftExtendsFromTheCursor()
    {
        var cursor = Cursor("Hello world", 6);
        _selection.SetMark(cursor); // "w"
        cursor.MoveTo(4);

        _selection.SelectFromMark(cursor);
        Assert.Equal("o w", _selection.TextFor(cursor));
        Assert.Equal(4, _selection.Active);

        var change = _selection.Extend(cursor, NavigationCommand.SelectPrevChar);
        Assert.Equal(new SelectionChange("l", true), change);
        Assert.Equal("lo w", _selection.TextFor(cursor));
    }

    [Fact]
    public void SelectFromMark_OnTheMark_SelectsItsCharacter_AndTheMarkIsPerDocument()
    {
        var cursor = Cursor("Hello world", 10);
        _selection.SetMark(cursor);

        Assert.True(_selection.SelectFromMark(cursor));
        Assert.Equal("d", _selection.TextFor(cursor));

        // Another document has no mark
        cursor.SetDocument(BuildDoc("Hello world"), 10);
        Assert.Null(_selection.MarkFor(cursor.Document));
        Assert.False(_selection.SelectFromMark(cursor));
    }

    [Fact]
    public void RebaseMark_ShiftsAMarkAfterTheChange_AndMovesOneInsideItToItsStart()
    {
        var doc = BuildDoc("Hello world");
        var cursor = new VBufferCursor(doc, Mock.Of<IAudioCuePlayer>());
        cursor.MoveTo(6);
        _selection.SetMark(cursor);

        var inserted = BuildDoc(">> Hello world");
        _selection.RebaseMark(doc, inserted, 0, 0, 3);
        Assert.Equal(9, _selection.MarkFor(inserted));

        var replaced = BuildDoc(">> Hello there");
        _selection.RebaseMark(inserted, replaced, 9, 14, 0);
        Assert.Equal(9, _selection.MarkFor(replaced));

        // An update of another document leaves the mark alone
        _selection.RebaseMark(inserted, BuildDoc("x"), 0, 0, 1);
        Assert.Equal(9, _selection.MarkFor(replaced));
    }
}
