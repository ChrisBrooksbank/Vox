using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.E2E.Tests;

/// <summary>The transcript comparison itself (ordinary unit tests: no desktop needed).</summary>
public class TranscriptTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vox-transcripts-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static IEnumerable<SpokenUtterance> Said(params (string Text, SpeechPriority Priority)[] items) =>
        items.Select(i => new SpokenUtterance(i.Text, i.Priority, DateTimeOffset.UnixEpoch));

    [Fact]
    public void Format_IsOneUtterancePerLineWithItsPriority()
    {
        var text = Transcript.Format(Said(("Section two, heading level 2", SpeechPriority.Interrupt), ("two\nlines", SpeechPriority.Low)));

        Assert.Equal("[Interrupt] Section two, heading level 2\n[Low] two lines\n", text);
    }

    [Fact]
    public void Difference_NamesTheFirstLineThatDiffers()
    {
        Assert.Null(Transcript.Difference("[High] a\r\n[High] b\r\n", "[High] a\n[High] b\n"));
        Assert.Equal("line 2: expected \"[High] b\", got \"[High] c\"", Transcript.Difference("[High] a\n[High] b\n", "[High] a\n[High] c\n"));
        Assert.Equal("line 2: expected \"(nothing)\", got \"[Low] extra\"", Transcript.Difference("[High] a\n", "[High] a\n[Low] extra\n"));
        Assert.Equal("line 1: expected \"[High] a\", got \"(nothing)\"", Transcript.Difference("[High] a\n", ""));
    }

    [Fact]
    public void Verify_WithoutAnApprovedTranscript_WritesTheReceivedOneAndFails()
    {
        Assert.Throws<TranscriptMismatchException>(() => Transcript.Verify("[High] a\n", "scenario", _directory));

        Assert.Equal("[High] a\n", File.ReadAllText(Path.Combine(_directory, "scenario" + Transcript.ReceivedSuffix)));
    }

    [Fact]
    public void Verify_Matching_PassesAndRemovesAnOldReceivedFile()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "scenario" + Transcript.ApprovedSuffix), "[High] a\n");
        File.WriteAllText(Path.Combine(_directory, "scenario" + Transcript.ReceivedSuffix), "[High] old\n");

        Transcript.Verify("[High] a\n", "scenario", _directory);

        Assert.False(File.Exists(Path.Combine(_directory, "scenario" + Transcript.ReceivedSuffix)));
    }
}
