using Xunit;

namespace Vox.E2E.Tests.Performance;

/// <summary>The performance gate itself (ordinary unit tests: no desktop needed).</summary>
public class PerformanceGateTests
{
    private static PerformanceReport Report(double firstSpeech, params double[] latencies) =>
        new(DateTimeOffset.UnixEpoch, "TEST", "Windows", "Edge", "1.0", [new PagePerformance("large-table.html", firstSpeech, latencies)]);

    [Theory]
    [InlineData(new double[] { 10 }, 95, 10)]
    [InlineData(new double[] { 5, 1, 3, 2, 4 }, 50, 3)]
    [InlineData(new double[] { 5, 1, 3, 2, 4 }, 100, 5)]
    [InlineData(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 }, 95, 19)]
    public void Percentile_IsNearestRank(double[] samples, double percent, double expected) =>
        Assert.Equal(expected, Percentile.Of(samples, percent));

    [Fact]
    public void Percentile_OfNothing_IsNone() => Assert.Null(Percentile.Of([], 95));

    [Fact]
    public void TheReport_HasTheP95OverEveryPage()
    {
        var report = new PerformanceReport(DateTimeOffset.UnixEpoch, "TEST", "Windows", "Edge", "1.0",
        [
            new PagePerformance("a.html", 900, [.. Enumerable.Range(1, 10).Select(i => (double)i)]),
            new PagePerformance("b.html", 900, [.. Enumerable.Range(11, 10).Select(i => (double)i)]),
        ]);

        Assert.Equal(19, report.KeypressP95Ms);
        Assert.Contains("Keypress latency p95: 19 ms (20 keys)", report.Summary());
    }

    [Fact]
    public void WithoutABaseline_NothingFails()
    {
        Assert.Empty(PerformanceGate.Check(Report(99_999, 5_000), new PerformanceBaseline()));
    }

    [Fact]
    public void WithinTheMargins_Passes()
    {
        var baseline = new PerformanceBaseline
        {
            KeypressP95Ms = 100,
            TimeToFirstSpeechMs = new() { ["large-table.html"] = 2000 },
        };

        // 100 ms + 30 ms (more than 25%); 2,000 ms + 25% (more than 300 ms)
        Assert.Empty(PerformanceGate.Check(Report(2500, 130), baseline));
    }

    [Fact]
    public void BeyondTheMargins_Fails_NamingTheFigure()
    {
        var baseline = new PerformanceBaseline
        {
            KeypressP95Ms = 100,
            TimeToFirstSpeechMs = new() { ["large-table.html"] = 2000 },
        };

        var regressions = PerformanceGate.Check(Report(2600, 131), baseline);

        Assert.Equal(["Keypress latency p95", "Time to first speech, large-table.html"], regressions.Select(r => r.Figure));
        Assert.Equal("Keypress latency p95: 131 ms, baseline 100 ms, at most 130 ms allowed", regressions[0].ToString());
    }

    [Fact]
    public void SmallFigures_GetTheAbsoluteMargin()
    {
        // 20 ms + 25% would be 25 ms; the 30 ms margin allows up to 50 ms
        Assert.Equal(50, PerformanceGate.Allowed(20, 25, 30));
        var baseline = new PerformanceBaseline { KeypressP95Ms = 20 };
        Assert.Empty(PerformanceGate.Check(Report(0, 45), baseline));
        Assert.Single(PerformanceGate.Check(Report(0, 51), baseline));
    }

    [Fact]
    public void ARun_BecomesTheBaseline_AndRoundTrips()
    {
        var baseline = new PerformanceBaseline { MarginPercent = 20 }.From(Report(1234.4, 40, 60, 80));

        var read = PerformanceBaseline.Parse(baseline.ToJson());

        Assert.Equal(80, read.KeypressP95Ms);
        Assert.Equal(1234, read.TimeToFirstSpeechMs["large-table.html"]);
        Assert.Equal(20, read.MarginPercent);
        Assert.StartsWith("TEST, Windows, Edge, Vox 1.0", read.RecordedOn);
        Assert.Empty(PerformanceGate.Check(Report(1234.4, 40, 60, 80), read));
    }

    [Fact]
    public void TheCheckedInBaseline_Reads()
    {
        var baseline = PerformanceBaseline.Load(PerformanceTests.BaselinePath);

        Assert.True(File.Exists(PerformanceTests.BaselinePath));
        Assert.True(baseline.MarginPercent > 0);
        Assert.True(baseline.KeypressMarginMs > 0);
        Assert.True(baseline.FirstSpeechMarginMs > 0);
    }

    [Fact]
    public void EveryReadingKeyCanBePressed()
    {
        foreach (var key in PerformanceTests.ReadingKeys)
            KeyNotation.Parse(key);
    }
}
