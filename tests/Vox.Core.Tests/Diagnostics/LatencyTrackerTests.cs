using Vox.Core.Diagnostics;
using Xunit;

namespace Vox.Core.Tests.Diagnostics;

public class LatencyTrackerTests
{
    [Fact]
    public void Percentile_NearestRank()
    {
        var tracker = new LatencyTracker();
        for (int ms = 1; ms <= 100; ms++)
            tracker.Record("stage", TimeSpan.FromMilliseconds(ms));

        Assert.Equal(95, tracker.Percentile("stage", 0.95));
        Assert.Equal(50, tracker.Percentile("stage", 0.5));
        Assert.Equal(100, tracker.Percentile("stage", 1));
        Assert.Equal(1, tracker.Percentile("stage", 0));
    }

    [Fact]
    public void Percentile_NoSamples_IsNull()
    {
        Assert.Null(new LatencyTracker().Percentile("stage", 0.95));
    }

    [Fact]
    public void KeepsOnlyTheMostRecentSamples()
    {
        var tracker = new LatencyTracker(capacity: 10);
        for (int i = 0; i < 10; i++)
            tracker.Record("stage", TimeSpan.FromMilliseconds(1000));
        for (int i = 0; i < 10; i++)
            tracker.Record("stage", TimeSpan.FromMilliseconds(5));

        Assert.Equal(10, tracker.Count("stage"));
        Assert.Equal(5, tracker.Percentile("stage", 1));
    }

    [Fact]
    public void KeyToSpeech_MeasuresTheNextSpeechAfterAKey_Once()
    {
        long now = 1000;
        var tracker = new LatencyTracker(clockMs: () => now);

        tracker.NoteKeyPress();
        now += 40;
        tracker.NoteSpeechStarted();
        now += 10;
        tracker.NoteSpeechStarted(); // follow-up speech, not caused by a key

        Assert.Equal(1, tracker.Count(LatencyTracker.KeyToSpeech));
        Assert.Equal(40, tracker.Percentile(LatencyTracker.KeyToSpeech, 0.95));
    }

    [Fact]
    public void KeyToSpeech_SpeechMuchLaterIsNotCounted()
    {
        long now = 1000;
        var tracker = new LatencyTracker(clockMs: () => now);

        tracker.NoteKeyPress();
        now += 5000;
        tracker.NoteSpeechStarted();

        Assert.Equal(0, tracker.Count(LatencyTracker.KeyToSpeech));
    }

    [Fact]
    public void Summary_ListsEachStage()
    {
        var tracker = new LatencyTracker();
        tracker.Record(LatencyTracker.KeyToSpeech, TimeSpan.FromMilliseconds(30));

        Assert.Equal("key to speech p95 30 ms (n=1)", tracker.Summary());
    }
}
