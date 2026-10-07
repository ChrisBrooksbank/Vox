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
}
