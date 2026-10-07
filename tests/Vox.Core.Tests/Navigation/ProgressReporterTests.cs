using Vox.Core.Configuration;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class ProgressReporterTests
{
    private VoxSettings _settings = new();
    private int? _foreground = 100;
    private DateTimeOffset _now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private ProgressReporter Reporter() => new(() => _settings, () => _foreground, () => _now);

    private static ProgressChangedEvent Progress(double value, int pid = 100, int[]? id = null, double max = 100) =>
        new(DateTimeOffset.UtcNow, id ?? [1], value, 0, max, pid);

    private static List<string?> SpeechFor(ProgressReporter reporter, params double[] values) =>
        values.Select(v => reporter.Evaluate(Progress(v))?.Speech).Where(s => s is not null).ToList();

    [Fact]
    public void Every10Percent_SpeaksEachStepOnce()
    {
        var reporter = Reporter();

        Assert.Equal(["0 percent", "10 percent", "20 percent", "100 percent"],
            SpeechFor(reporter, 0, 3, 9, 10, 15, 20, 21, 100));
    }

    [Fact]
    public void Every25Percent()
    {
        _settings = _settings with { ProgressBars = ProgressReporting.Every25Percent };

        Assert.Equal(["5 percent", "30 percent", "50 percent"], SpeechFor(Reporter(), 5, 10, 24, 30, 49, 50));
    }

    [Fact]
    public void FractionalRange_IsConvertedToPercent()
    {
        var reporter = Reporter();
        reporter.Evaluate(Progress(0.0, max: 1));

        Assert.Equal("50 percent", reporter.Evaluate(Progress(0.5, max: 1))?.Speech);
    }

    [Fact]
    public void Off_SaysNothing()
    {
        _settings = _settings with { ProgressBars = ProgressReporting.Off };

        Assert.Null(Reporter().Evaluate(Progress(50)));
    }

    [Fact]
    public void BackgroundApplication_IgnoredUnlessEnabled()
    {
        Assert.Null(Reporter().Evaluate(Progress(50, pid: 200)));

        _settings = _settings with { ReportBackgroundProgress = true };
        Assert.NotNull(Reporter().Evaluate(Progress(50, pid: 200)));
    }

    [Fact]
    public void EachProgressBarIsFollowedSeparately()
    {
        var reporter = Reporter();
        reporter.Evaluate(Progress(10, id: [1]));

        Assert.Equal("10 percent", reporter.Evaluate(Progress(10, id: [2]))?.Speech);
    }

    [Fact]
    public void Beep_RisesWithProgress_AndIsThrottled()
    {
        _settings = _settings with { ProgressBars = ProgressReporting.Beep };
        var reporter = Reporter();

        Assert.Equal(110, reporter.Evaluate(Progress(0))?.ToneHz);
        Assert.Null(reporter.Evaluate(Progress(5)));                   // within 100 ms
        _now += TimeSpan.FromMilliseconds(150);
        Assert.Equal(ProgressReporter.ToneFor(50), reporter.Evaluate(Progress(50))?.ToneHz);
        _now += TimeSpan.FromMilliseconds(150);
        Assert.Null(reporter.Evaluate(Progress(50)));                  // unchanged
        Assert.Equal(1760, ProgressReporter.ToneFor(100), 3);
    }
}
