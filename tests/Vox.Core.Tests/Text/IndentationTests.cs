using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Text;
using Xunit;

namespace Vox.Core.Tests.Text;

public class IndentationTests
{
    // Lines start at 0, 13, 26, 27, 43
    private const string Code = "if (ready) {\n    start();\n\n    if (done) {\n\t\tstop();\n}";

    private readonly DateTimeOffset _now = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);

    private (TextCaretTracker Tracker, List<int> Tones) Tracker(IndentationReporting mode)
    {
        var tracker = new TextCaretTracker(() => _now, () => SpellingErrorReporting.Off, () => mode);
        var tones = new List<int>();
        tracker.IndentationChanged += (_, columns) => tones.Add(columns);
        return (tracker, tones);
    }

    private static string? Down(TextCaretTracker tracker, int caret)
    {
        tracker.NoteKey(CaretKeys.VK_DOWN, KeyModifiers.None);
        return tracker.OnCaretMoved(new StringTextDocument(Code, caret));
    }

    [Fact]
    public void Speech_SaysTheIndentationWhenItChanges()
    {
        var (tracker, tones) = Tracker(IndentationReporting.Speech);

        Assert.Equal("no indent, if (ready) {", Down(tracker, 0));
        Assert.Equal("4 spaces,     start();", Down(tracker, 13));
        Assert.Equal("blank", Down(tracker, 26));          // blank lines don't count
        Assert.Equal("    if (done) {", Down(tracker, 28));  // unchanged
        Assert.Equal("2 tabs, \t\tstop();", Down(tracker, 44));
        Assert.Empty(tones);
    }

    [Fact]
    public void Tones_PlayInsteadOfSpeech()
    {
        var (tracker, tones) = Tracker(IndentationReporting.Tones);

        Assert.Equal("if (ready) {", Down(tracker, 0));
        Assert.Equal("    start();", Down(tracker, 13));
        Assert.Equal("\t\tstop();", Down(tracker, 44));

        Assert.Equal([0, 4, 8], tones);
    }

    [Fact]
    public void Both_SpeaksAndPlays()
    {
        var (tracker, tones) = Tracker(IndentationReporting.Both);

        Assert.Equal("4 spaces,     start();", Down(tracker, 13));
        Assert.Equal([4], tones);
    }

    [Fact]
    public void Off_ReportsNothing()
    {
        var (tracker, tones) = Tracker(IndentationReporting.Off);

        Assert.Equal("    start();", Down(tracker, 13));
        Assert.Empty(tones);
    }

    [Fact]
    public void AfterAFocusChange_TheFirstLineIsReportedAgain()
    {
        var (tracker, _) = Tracker(IndentationReporting.Speech);
        Down(tracker, 13);

        tracker.Reset();

        Assert.Equal("4 spaces,     start();", Down(tracker, 13));
    }

    [Theory]
    [InlineData("x", 0, "no indent")]
    [InlineData("  x", 2, "2 spaces")]
    [InlineData(" x", 1, "1 space")]
    [InlineData("\tx", 4, "1 tab")]
    [InlineData("\t  x", 6, "1 tab 2 spaces")]
    [InlineData("  \tx", 4, "1 tab 2 spaces")]
    public void Indentation_Measure(string line, int columns, string description)
    {
        Assert.Equal((columns, description), TextCaretTracker.Indentation(line));
    }

    [Fact]
    public void Tones_RiseWithIndentation()
    {
        Assert.Equal(220, TextCaretTracker.IndentationToneHz(0));
        Assert.True(TextCaretTracker.IndentationToneHz(8) > TextCaretTracker.IndentationToneHz(4));
        Assert.Equal(2000, TextCaretTracker.IndentationToneHz(200));
    }
}
