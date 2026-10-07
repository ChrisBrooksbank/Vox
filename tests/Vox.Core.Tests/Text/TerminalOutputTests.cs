using Vox.Core.Text;
using Xunit;

namespace Vox.Core.Tests.Text;

public class TerminalOutputTests
{
    private DateTimeOffset _now = new(2026, 10, 7, 11, 0, 0, TimeSpan.Zero);

    private TerminalOutputTracker Tracker(params string[] initial)
    {
        var tracker = new TerminalOutputTracker(() => _now);
        tracker.Reset(initial);
        return tracker;
    }

    [Fact]
    public void NewLinesBelowThePrompt_AreOutput()
    {
        var tracker = Tracker("PS C:\\> dir", "", "", "");

        var output = tracker.Update(["PS C:\\> dir", "file1.txt", "file2.txt", "PS C:\\> "]);

        Assert.Equal(["file1.txt", "file2.txt", "PS C:\\>"], output);
    }

    [Fact]
    public void ScrolledScreen_OnlyTheNewLinesAreOutput()
    {
        var tracker = Tracker("line 1", "line 2", "line 3", "$ ls");

        var output = tracker.Update(["line 3", "$ ls", "a.txt", "$ "]);

        Assert.Equal(["a.txt", "$"], output);
    }

    [Fact]
    public void UnchangedScreen_NoOutput()
    {
        var tracker = Tracker("a", "b");

        Assert.Empty(tracker.Update(["a", "b"]));
    }

    [Fact]
    public void TypedCharactersEchoedOnThePrompt_AreNotOutput()
    {
        var tracker = Tracker("$ ", "");
        tracker.NoteKeyPress();
        tracker.NoteKeyPress();

        Assert.Empty(tracker.Update(["$ ls", ""]));
    }

    [Fact]
    public void Backspace_OnThePrompt_IsNotOutput()
    {
        var tracker = Tracker("$ lsx", "");
        tracker.NoteKeyPress();

        Assert.Empty(tracker.Update(["$ ls", ""]));
    }

    [Fact]
    public void MoreTextThanWasTyped_IsOutput()
    {
        // Tab completion: one key, many characters
        var tracker = Tracker("$ cd Doc", "");
        tracker.NoteKeyPress();

        Assert.Equal(["$ cd Documents"], tracker.Update(["$ cd Documents", ""]));
    }

    [Fact]
    public void ChangeLongAfterTheKey_IsOutput()
    {
        var tracker = Tracker("progress: 1", "");
        tracker.NoteKeyPress();
        _now += TimeSpan.FromSeconds(2);

        Assert.Equal(["progress: 2"], tracker.Update(["progress: 2", ""]));
    }

    [Fact]
    public void Throttle_OneUtterancePerInterval()
    {
        var throttle = new TerminalSpeechThrottle();
        throttle.Add(["first"]);
        Assert.Equal("first", throttle.Flush(_now));

        throttle.Add(["second"]);
        Assert.Null(throttle.Flush(_now + TimeSpan.FromMilliseconds(50)));
        throttle.Add(["third"]);
        Assert.Equal("second\nthird", throttle.Flush(_now + TimeSpan.FromMilliseconds(100)));
        Assert.Null(throttle.Flush(_now + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Throttle_LongBurst_IsSummarisedWithItsLastLine()
    {
        var throttle = new TerminalSpeechThrottle();
        throttle.Add(Enumerable.Range(1, 25).Select(i => $"line {i}"));

        Assert.Equal("25 lines of output. line 25", throttle.Flush(_now));
    }
}
