using System.Runtime.InteropServices;
using Vox.Core.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace Vox.E2E.Tests.Performance;

/// <summary>
/// The performance run (docs/performance.md): for every corpus page (the 10,000-row table
/// included) in Edge, the time from opening the page to Vox saying its title, and the latency
/// from each key press to the speech engine receiving what Vox says about it. Writes the figures
/// to <c>VOX_PERF_REPORT</c> (default <c>%TEMP%\vox-performance.json</c>) and fails when they are
/// worse than <c>Performance/baseline.json</c> beyond its margins; <c>VOX_PERF_UPDATE_BASELINE=1</c>
/// writes the figures into the baseline instead. Needs VOX_E2E=1 like every end-to-end test.
/// </summary>
public class PerformanceTests(ITestOutputHelper output)
{
    /// <summary>The keys pressed on every page after its own scenario's, for more samples of plain reading.</summary>
    public static readonly string[] ReadingKeys = ["Down", "Down", "Down", "Down", "Down", "Up", "Up", "Up", "Insert+Up", "Ctrl+Home"];

    /// <summary>How long a key may take to be answered before it is counted as saying nothing.</summary>
    public static readonly TimeSpan KeyTimeout = TimeSpan.FromSeconds(3);

    public static string BaselinePath { get; } =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Performance", "baseline.json"));

    [E2EFact]
    public async Task Figures_StayWithinTheBaseline()
    {
        var pages = new List<PagePerformance>();
        foreach (var scenario in CorpusScenarios.All.DistinctBy(s => s.Page))
        {
            pages.Add(await MeasureAsync(scenario));
            output.WriteLine($"{scenario.Page}: done");
        }

        var report = new PerformanceReport(DateTimeOffset.UtcNow, Environment.MachineName, RuntimeInformation.OSDescription,
            Browser.Edge.ToString(), typeof(Vox.App.ServiceRegistration).Assembly.GetName().Version?.ToString() ?? "?", pages);
        var reportPath = Environment.GetEnvironmentVariable("VOX_PERF_REPORT") is { Length: > 0 } path
            ? path
            : Path.Combine(Path.GetTempPath(), "vox-performance.json");
        await File.WriteAllTextAsync(reportPath, report.ToJson());
        output.WriteLine(report.Summary());
        output.WriteLine($"Report: {reportPath}");

        var baseline = PerformanceBaseline.Load(BaselinePath);
        if (Environment.GetEnvironmentVariable("VOX_PERF_UPDATE_BASELINE") == "1")
        {
            await File.WriteAllTextAsync(BaselinePath, baseline.From(report).ToJson());
            output.WriteLine($"Baseline written: {BaselinePath}");
            return;
        }
        var regressions = PerformanceGate.Check(report, baseline);
        Assert.True(regressions.Count == 0, "Slower than the baseline:" + Environment.NewLine + string.Join(Environment.NewLine, regressions));
    }

    private static async Task<PagePerformance> MeasureAsync(Scenario scenario)
    {
        await using var vox = await VoxHarness.StartAsync();
        var context = new ScenarioContext(vox, ScenarioPacers.None);

        // Time to first speech: from asking the browser for the page to Vox saying its title
        var opened = DateTimeOffset.UtcNow;
        using var session = BrowserSession.Open(scenario.Page, Browser.Edge);
        var title = await vox.Speech.WaitForAsync(s => s.Timestamp >= opened && s.Text.Contains(scenario.Title), CorpusScenarios.PageLoad);
        var firstSpeech = (title.Timestamp - opened).TotalMilliseconds;
        context.Target = await session.WindowAsync(TimeSpan.FromSeconds(5));
        await context.QuietAsync();

        var latencies = new List<double>();
        foreach (var key in scenario.Keys.Concat(ReadingKeys))
        {
            if (await LatencyAsync(vox.Speech, key) is { } latency)
                latencies.Add(latency);
            await context.QuietAsync();
        }
        return new PagePerformance(scenario.Page, firstSpeech, latencies);
    }

    /// <summary>From pressing <paramref name="key"/> to the first thing said after it (ms), or null when nothing was.</summary>
    public static async Task<double?> LatencyAsync(RecordingSpeechEngine speech, string key)
    {
        var pressed = DateTimeOffset.UtcNow;
        KeyNotation.Press(key);
        try
        {
            var said = await speech.WaitForAsync(s => s.Timestamp >= pressed, KeyTimeout);
            return (said.Timestamp - pressed).TotalMilliseconds;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }
}
