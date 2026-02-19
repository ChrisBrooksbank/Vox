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
}
