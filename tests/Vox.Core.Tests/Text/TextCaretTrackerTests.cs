using Vox.Core.Input;
using Vox.Core.Text;
using Xunit;

namespace Vox.Core.Tests.Text;

public class TextCaretTrackerTests
{
    private const string Text = "The quick brown fox\njumps over\n\nthe lazy dog";

    private DateTimeOffset _now = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private TextCaretTracker Tracker() => new(() => _now);

    private static StringTextDocument CaretAt(int offset) => new(Text, offset);

    [Theory]
    [InlineData(CaretKeys.VK_RIGHT, KeyModifiers.None, 1, "h")]
    [InlineData(CaretKeys.VK_LEFT, KeyModifiers.None, 3, "Space")]
    [InlineData(CaretKeys.VK_RIGHT, KeyModifiers.None, 19, "blank")]          // on the line break
    [InlineData(CaretKeys.VK_DOWN, KeyModifiers.None, 20, "jumps over")]
    [InlineData(CaretKeys.VK_UP, KeyModifiers.None, 5, "The quick brown fox")]
    [InlineData(CaretKeys.VK_DOWN, KeyModifiers.None, 31, "blank")]           // the empty line
    [InlineData(CaretKeys.VK_RIGHT, KeyModifiers.Ctrl, 10, "brown")]
    [InlineData(CaretKeys.VK_LEFT, KeyModifiers.Ctrl, 4, "quick")]
    [InlineData(CaretKeys.VK_DOWN, KeyModifiers.Ctrl, 32, "the lazy dog")]
    [InlineData(CaretKeys.VK_HOME, KeyModifiers.None, 20, "jumps over")]
    [InlineData(CaretKeys.VK_END, KeyModifiers.Ctrl, 44, "the lazy dog")]
    [InlineData(CaretKeys.VK_NEXT, KeyModifiers.None, 32, "the lazy dog")]
    public void CaretKey_ReadsItsUnitAtTheNewCaret(int vk, KeyModifiers modifiers, int newCaret, string expected)
    {
        var tracker = Tracker();

        Assert.True(tracker.NoteKey(vk, modifiers));

        Assert.Equal(expected, tracker.OnCaretMoved(CaretAt(newCaret)));
    }

    [Fact]
    public void CaretMoveWithoutACaretKey_SaysNothing()
    {
        var tracker = Tracker();
        tracker.NoteKey('A', KeyModifiers.None); // typing

        Assert.Null(tracker.OnCaretMoved(CaretAt(1)));
    }

    [Fact]
    public void SecondCaretEventForTheSameKey_SaysNothing()
    {
        var tracker = Tracker();
        tracker.NoteKey(CaretKeys.VK_RIGHT, KeyModifiers.None);

        Assert.NotNull(tracker.OnCaretMoved(CaretAt(1)));
        Assert.Null(tracker.OnCaretMoved(CaretAt(1)));
    }

    [Fact]
    public void StaleKey_SaysNothing()
    {
        var tracker = Tracker();
        tracker.NoteKey(CaretKeys.VK_DOWN, KeyModifiers.None);
        _now += TimeSpan.FromSeconds(2);

        Assert.Null(tracker.OnCaretMoved(CaretAt(20)));
    }

    [Fact]
    public void Reset_ForgetsThePendingKey()
    {
        var tracker = Tracker();
        tracker.NoteKey(CaretKeys.VK_DOWN, KeyModifiers.None);
        tracker.Reset();

        Assert.Null(tracker.OnCaretMoved(CaretAt(20)));
    }

    [Theory]
    [InlineData(CaretKeys.VK_LEFT, KeyModifiers.Alt)]
    [InlineData(CaretKeys.VK_DOWN, KeyModifiers.Insert)]
    [InlineData(0x41, KeyModifiers.None)]
    public void NotACaretKey(int vk, KeyModifiers modifiers)
    {
        Assert.False(Tracker().NoteKey(vk, modifiers));
    }

    // -------------------------------------------------------------------------
    // Selection
    // -------------------------------------------------------------------------

    private const string Sentence = "hello big world";

    private static StringTextDocument Selected(int start, int end, int? caret = null) =>
        new(Sentence, caret ?? end, start, end);

    private string? Press(TextCaretTracker tracker, int vk, KeyModifiers modifiers, StringTextDocument after)
    {
        tracker.NoteKey(vk, modifiers);
        return tracker.OnCaretMoved(after);
    }

    [Fact]
    public void ShiftRight_SaysTheSelectedCharacter()
    {
        var tracker = Tracker();
        tracker.OnCaretMoved(Selected(0, 0)); // the caret is known before selecting

        Assert.Equal("selected h", Press(tracker, CaretKeys.VK_RIGHT, KeyModifiers.Shift, Selected(0, 1)));
        Assert.Equal("selected e", Press(tracker, CaretKeys.VK_RIGHT, KeyModifiers.Shift, Selected(0, 2)));
    }

    [Fact]
    public void ShiftLeftAfterShiftRight_SaysUnselected()
    {
        var tracker = Tracker();
        tracker.OnCaretMoved(Selected(0, 0));
        Press(tracker, CaretKeys.VK_RIGHT, KeyModifiers.Shift, Selected(0, 2));

        Assert.Equal("unselected e", Press(tracker, CaretKeys.VK_LEFT, KeyModifiers.Shift, Selected(0, 1)));
    }

    [Fact]
    public void CtrlShiftRight_SaysTheSelectedWord()
    {
        var tracker = Tracker();
        tracker.OnCaretMoved(Selected(6, 6));

        Assert.Equal("selected big", Press(tracker, CaretKeys.VK_RIGHT, KeyModifiers.Ctrl | KeyModifiers.Shift, Selected(6, 10)));
    }

    [Fact]
    public void ShiftLeft_ExtendingBackwards_SaysSelected()
    {
        var tracker = Tracker();
        tracker.OnCaretMoved(Selected(10, 10));

        Assert.Equal("selected Space", Press(tracker, CaretKeys.VK_LEFT, KeyModifiers.Shift, Selected(9, 10, caret: 9)));
    }

    [Fact]
    public void ShiftKeyCollapsingTheSelection_SaysUnselected()
    {
        var tracker = Tracker();
        tracker.OnCaretMoved(Selected(0, 0));
        Press(tracker, CaretKeys.VK_RIGHT, KeyModifiers.Shift, Selected(0, 1));

        Assert.Equal("unselected h", Press(tracker, CaretKeys.VK_LEFT, KeyModifiers.Shift, Selected(0, 0)));
    }

    [Fact]
    public void ShiftKeyWithNothingKnownBefore_SaysEverythingSelected()
    {
        var tracker = Tracker();

        Assert.Equal("selected hello", Press(tracker, CaretKeys.VK_RIGHT, KeyModifiers.Ctrl | KeyModifiers.Shift, Selected(0, 5)));
    }

    [Fact]
    public void CtrlA_SaysAllSelected()
    {
        var tracker = Tracker();

        Assert.Equal("all selected", Press(tracker, 0x41, KeyModifiers.Ctrl, Selected(0, Sentence.Length)));
    }

    [Fact]
    public void CtrlA_InAControlThatDoesntSelectAll_SaysNothing()
    {
        Assert.Null(Press(Tracker(), 0x41, KeyModifiers.Ctrl, Selected(0, 0)));
    }

    [Fact]
    public void ShiftDown_SaysTheSelectedLine()
    {
        var tracker = Tracker();
        tracker.OnCaretMoved(new StringTextDocument(Text, 0));

        var spoken = Press(tracker, CaretKeys.VK_DOWN, KeyModifiers.Shift, new StringTextDocument(Text, 20, 0, 20));

        Assert.Equal("selected The quick brown fox", spoken);
    }

    // -------------------------------------------------------------------------
    // Deletion echo
    // -------------------------------------------------------------------------

    private const int VK_BACK = 0x08, VK_DELETE = 0x2E;

    private string? Delete(TextCaretTracker tracker, int vk, KeyModifiers modifiers, StringTextDocument after,
        bool caretEventFirst = true)
    {
        tracker.NoteKey(vk, modifiers);
        if (caretEventFirst)
            Assert.Null(tracker.OnCaretMoved(after)); // the caret event comes before the text change
        return tracker.OnTextChanged(after);
    }

    [Fact]
    public void Backspace_SaysTheDeletedCharacter()
    {
        var tracker = Tracker();
        tracker.Prime(new StringTextDocument("hello", 5));

        Assert.Equal("o", Delete(tracker, VK_BACK, KeyModifiers.None, new StringTextDocument("hell", 4)));
        Assert.Equal("l", Delete(tracker, VK_BACK, KeyModifiers.None, new StringTextDocument("hel", 3)));
    }

    [Fact]
    public void Delete_SaysTheDeletedCharacter_EvenWithoutACaretEvent()
    {
        var tracker = Tracker();
        tracker.Prime(new StringTextDocument("a, b", 1));

        Assert.Equal("comma", Delete(tracker, VK_DELETE, KeyModifiers.None, new StringTextDocument("a b", 1), caretEventFirst: false));
    }

    [Fact]
    public void CtrlBackspace_SaysTheDeletedWord()
    {
        var tracker = Tracker();
        tracker.Prime(new StringTextDocument("hello big world", 10));

        Assert.Equal("big", Delete(tracker, VK_BACK, KeyModifiers.Ctrl, new StringTextDocument("hello world", 6)));
    }

    [Fact]
    public void BackspaceAtLineStart_SaysLineBreak()
    {
        var tracker = Tracker();
        tracker.Prime(new StringTextDocument("one\ntwo", 4));

        Assert.Equal("line break", Delete(tracker, VK_BACK, KeyModifiers.None, new StringTextDocument("onetwo", 3)));
    }

    [Fact]
    public void TextChangeWithoutDeletionKey_SaysNothingButKeepsTheLine()
    {
        var tracker = Tracker();
        tracker.Prime(new StringTextDocument("ab", 2));
        tracker.NoteKey('C', KeyModifiers.None);

        Assert.Null(tracker.OnTextChanged(new StringTextDocument("abc", 3)));
        Assert.Equal("c", Delete(tracker, VK_BACK, KeyModifiers.None, new StringTextDocument("ab", 2)));
    }

    [Fact]
    public void DeletionWithNothingKnownBefore_SaysNothing()
    {
        Assert.Null(Delete(Tracker(), VK_BACK, KeyModifiers.None, new StringTextDocument("ab", 2)));
    }
}
