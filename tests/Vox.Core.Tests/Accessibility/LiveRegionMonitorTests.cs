using Vox.Core.Accessibility;
using Vox.Core.Pipeline;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class LiveRegionMonitorTests
{
    // -------------------------------------------------------------------------
    // Diff detection
    // -------------------------------------------------------------------------

    [Fact]
    public void ShouldAnnounce_NewText_ReturnsTrue()
    {
        var monitor = new LiveRegionMonitor();
        var result = monitor.ShouldAnnounce("src1", "Hello", LiveRegionPoliteness.Polite);
        Assert.True(result);
    }

    [Fact]
    public void ShouldAnnounce_SameTextTwice_ReturnsFalseSecondTime()
    {
        var monitor = new LiveRegionMonitor();
        monitor.ShouldAnnounce("src1", "Hello", LiveRegionPoliteness.Polite);

        // Same text — should not announce again
        var result = monitor.ShouldAnnounce("src1", "Hello", LiveRegionPoliteness.Polite);
        Assert.False(result);
    }

    [Fact]
    public void ShouldAnnounce_ChangedText_ReturnsTrue()
    {
        var time = DateTimeOffset.UtcNow;
        var monitor = new LiveRegionMonitor(() => time);

        monitor.ShouldAnnounce("src1", "Hello", LiveRegionPoliteness.Polite);
        time = time.AddSeconds(1); // advance past throttle window

        var result = monitor.ShouldAnnounce("src1", "World", LiveRegionPoliteness.Polite);
        Assert.True(result);
    }

    [Fact]
    public void ShouldAnnounce_EmptyText_ReturnsFalse()
    {
        var monitor = new LiveRegionMonitor();
        var result = monitor.ShouldAnnounce("src1", "", LiveRegionPoliteness.Polite);
        Assert.False(result);
    }

    [Fact]
    public void ShouldAnnounce_WhitespaceText_ReturnsFalse()
    {
        var monitor = new LiveRegionMonitor();
        var result = monitor.ShouldAnnounce("src1", "   ", LiveRegionPoliteness.Polite);
        Assert.False(result);
    }

    [Fact]
    public void ShouldAnnounce_NullSourceId_AlwaysAnnounces()
    {
        var monitor = new LiveRegionMonitor();
        // Without a sourceId we cannot diff, so always announce non-empty text
        var result = monitor.ShouldAnnounce(null, "Toast message", LiveRegionPoliteness.Polite);
        Assert.True(result);
    }

    // -------------------------------------------------------------------------
    // Polite throttling
    // -------------------------------------------------------------------------

    [Fact]
    public void ShouldAnnounce_PoliteWithin500ms_Throttled()
    {
        var time = DateTimeOffset.UtcNow;
        var monitor = new LiveRegionMonitor(() => time);

        // First announcement
        monitor.ShouldAnnounce("src1", "First", LiveRegionPoliteness.Polite);

        // Different text but within 500ms window
        time = time.AddMilliseconds(100);
        var result = monitor.ShouldAnnounce("src1", "Second", LiveRegionPoliteness.Polite);
        Assert.False(result);
    }

    [Fact]
    public void ShouldAnnounce_PoliteAfter500ms_Allowed()
    {
        var time = DateTimeOffset.UtcNow;
        var monitor = new LiveRegionMonitor(() => time);

        monitor.ShouldAnnounce("src1", "First", LiveRegionPoliteness.Polite);

        // Advance exactly past the 500ms cooldown
        time = time.AddMilliseconds(501);
        var result = monitor.ShouldAnnounce("src1", "Second", LiveRegionPoliteness.Polite);
        Assert.True(result);
    }

    [Fact]
    public void ShouldAnnounce_PoliteThrottleIsPerSource()
    {
        var time = DateTimeOffset.UtcNow;
        var monitor = new LiveRegionMonitor(() => time);

        // Throttle src1
        monitor.ShouldAnnounce("src1", "First", LiveRegionPoliteness.Polite);
        time = time.AddMilliseconds(100);

        // src2 has a different cooldown window — should be allowed
        var result = monitor.ShouldAnnounce("src2", "Other", LiveRegionPoliteness.Polite);
        Assert.True(result);
    }

    // -------------------------------------------------------------------------
    // Assertive bypass
    // -------------------------------------------------------------------------

    [Fact]
    public void ShouldAnnounce_AssertiveBypassesThrottle()
    {
        var time = DateTimeOffset.UtcNow;
        var monitor = new LiveRegionMonitor(() => time);

        // First polite announcement
        monitor.ShouldAnnounce("src1", "First", LiveRegionPoliteness.Polite);

        // Assertive with different text within 500ms — should still announce
        time = time.AddMilliseconds(100);
        var result = monitor.ShouldAnnounce("src1", "Alert!", LiveRegionPoliteness.Assertive);
        Assert.True(result);
    }

    [Fact]
    public void ShouldAnnounce_AssertiveSameText_ReturnsFalse()
    {
        var monitor = new LiveRegionMonitor();
        monitor.ShouldAnnounce("src1", "Alert!", LiveRegionPoliteness.Assertive);

        // Same text — even assertive should not repeat unchanged content
        var result = monitor.ShouldAnnounce("src1", "Alert!", LiveRegionPoliteness.Assertive);
        Assert.False(result);
    }

    [Fact]
    public void ShouldAnnounce_AssertiveChangedText_ReturnsTrue()
    {
        var time = DateTimeOffset.UtcNow;
        var monitor = new LiveRegionMonitor(() => time);

        monitor.ShouldAnnounce("src1", "First", LiveRegionPoliteness.Assertive);

        // Even within 500ms, assertive changed text should announce
        time = time.AddMilliseconds(50);
        var result = monitor.ShouldAnnounce("src1", "Second", LiveRegionPoliteness.Assertive);
        Assert.True(result);
    }

    // -------------------------------------------------------------------------
    // Reset
    // -------------------------------------------------------------------------

    [Fact]
    public void Reset_ClearsState_AllowsReannouncement()
    {
        var monitor = new LiveRegionMonitor();
        monitor.ShouldAnnounce("src1", "Hello", LiveRegionPoliteness.Assertive);

        monitor.Reset();

        // After reset, same text should be announced again
        var result = monitor.ShouldAnnounce("src1", "Hello", LiveRegionPoliteness.Assertive);
        Assert.True(result);
    }

    // -------------------------------------------------------------------------
    // Deferred polite updates, additions, bounded tracking
    // -------------------------------------------------------------------------

    [Fact]
    public void Evaluate_PoliteWithinCooldown_IsHeldAndFlushedLater()
    {
        var time = DateTimeOffset.UtcNow;
        var monitor = new LiveRegionMonitor(() => time);

        Assert.Equal("Loading", monitor.Evaluate("src", "Loading", LiveRegionPoliteness.Polite, out _));

        time = time.AddMilliseconds(200);
        Assert.Null(monitor.Evaluate("src", "Done", LiveRegionPoliteness.Polite, out var retry));
        Assert.Equal(TimeSpan.FromMilliseconds(300), retry);

        // Too early: still cooling down
        Assert.Null(monitor.FlushPending("src", out var retryAgain));
        Assert.True(retryAgain > TimeSpan.Zero);

        time = time.AddMilliseconds(300);
        Assert.Equal("Done", monitor.FlushPending("src", out _));
        Assert.Null(monitor.FlushPending("src", out _));
    }

    [Fact]
    public void Evaluate_AppendedText_AnnouncesOnlyTheAddition()
    {
        var monitor = new LiveRegionMonitor();

        monitor.Evaluate("chat", "Alice: hi", LiveRegionPoliteness.Assertive, out _);
        var added = monitor.Evaluate("chat", "Alice: hi Bob: hello", LiveRegionPoliteness.Assertive, out _);

        Assert.Equal("Bob: hello", added);
    }

    [Fact]
    public void Evaluate_ExtendedWordIsAChange_NotAnAddition()
    {
        var monitor = new LiveRegionMonitor();

        monitor.Evaluate("count", "1", LiveRegionPoliteness.Assertive, out _);
        Assert.Equal("12", monitor.Evaluate("count", "12", LiveRegionPoliteness.Assertive, out _));
    }

    [Fact]
    public void Evaluate_AdditionsDuringCooldown_Accumulate()
    {
        var time = DateTimeOffset.UtcNow;
        var monitor = new LiveRegionMonitor(() => time);

        monitor.Evaluate("log", "a", LiveRegionPoliteness.Polite, out _);
        time = time.AddMilliseconds(100);
        monitor.Evaluate("log", "a b", LiveRegionPoliteness.Polite, out _);
        monitor.Evaluate("log", "a b c", LiveRegionPoliteness.Polite, out _);

        time = time.AddMilliseconds(500);
        Assert.Equal("b c", monitor.FlushPending("log", out _));
    }

    [Fact]
    public void TrackedSources_AreBounded()
    {
        var monitor = new LiveRegionMonitor();

        for (int i = 0; i < LiveRegionMonitor.MaxSources + 50; i++)
            monitor.Evaluate($"src{i}", "text", LiveRegionPoliteness.Assertive, out _);

        Assert.Equal(LiveRegionMonitor.MaxSources, monitor.TrackedSourceCount);
    }
}

public class LiveRegionRepeatTests
{
    private DateTimeOffset _now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private LiveRegionMonitor CreateMonitor() => new(() => _now);

    [Fact]
    public void SameMessage_AfterRegionWasCleared_IsSpokenAgain()
    {
        var monitor = CreateMonitor();
        Assert.Equal("Saved", monitor.Evaluate("r", "Saved", LiveRegionPoliteness.Assertive, out _));

        Assert.Null(monitor.Evaluate("r", "", LiveRegionPoliteness.Assertive, out _));
        _now = _now.AddMilliseconds(100);

        Assert.Equal("Saved", monitor.Evaluate("r", "Saved", LiveRegionPoliteness.Assertive, out _));
    }

    [Fact]
    public void SameMessage_WithinRepeatInterval_IsADuplicate()
    {
        var monitor = CreateMonitor();
        monitor.Evaluate("r", "Saved", LiveRegionPoliteness.Assertive, out _);
        _now = _now.AddMilliseconds(LiveRegionMonitor.RepeatAfterMs / 2);

        Assert.Null(monitor.Evaluate("r", "Saved", LiveRegionPoliteness.Assertive, out _));
    }

    [Fact]
    public void AssertiveMessage_AfterAQuietGap_IsSpokenAgain()
    {
        var monitor = CreateMonitor();
        monitor.Evaluate("r", "Saved", LiveRegionPoliteness.Assertive, out _);
        _now = _now.AddMilliseconds(LiveRegionMonitor.RepeatAfterMs + 100);

        Assert.Equal("Saved", monitor.Evaluate("r", "Saved", LiveRegionPoliteness.Assertive, out _));
    }

    [Fact]
    public void PoliteRegionReRenderedWithSameText_IsNotRepeated()
    {
        var monitor = CreateMonitor();
        monitor.Evaluate("r", "Connected", LiveRegionPoliteness.Polite, out _);
        _now = _now.AddMilliseconds(LiveRegionMonitor.RepeatAfterMs * 3);

        Assert.Null(monitor.Evaluate("r", "Connected", LiveRegionPoliteness.Polite, out _));
    }

    [Fact]
    public void PoliteMessage_AfterClear_IsSpokenAgain()
    {
        var monitor = CreateMonitor();
        monitor.Evaluate("r", "Saved", LiveRegionPoliteness.Polite, out _);
        monitor.Evaluate("r", "", LiveRegionPoliteness.Polite, out _);
        _now = _now.AddMilliseconds(600); // past the polite cooldown

        Assert.Equal("Saved", monitor.Evaluate("r", "Saved", LiveRegionPoliteness.Polite, out _));
    }

    [Fact]
    public void RepeatedDuplicateEvents_NeverBecomeARepeat()
    {
        var monitor = CreateMonitor();
        monitor.Evaluate("r", "Saved", LiveRegionPoliteness.Assertive, out _);
        for (int i = 0; i < 5; i++)
        {
            _now = _now.AddMilliseconds(LiveRegionMonitor.RepeatAfterMs / 2);
            Assert.Null(monitor.Evaluate("r", "Saved", LiveRegionPoliteness.Assertive, out _));
        }
    }
}
