using System.Diagnostics;
using System.Globalization;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Vox.E2E.Tests;

/// <summary>One look at the test process (Vox runs in it): memory and handles after a cycle.</summary>
public sealed record SoakSample(DateTimeOffset At, int Cycle, long PrivateBytes, long ManagedHeap, int Handles, string? Failure = null);

/// <summary>Whether a soak run's samples show something growing that shouldn't.</summary>
public static class SoakAnalysis
{
    /// <summary>Samples before this many cycles are warm-up (caches filling) and not compared.</summary>
    public const int WarmUpCycles = 3;

    /// <summary>
    /// The problems in <paramref name="samples"/>: comparing the median of the first quarter (after
    /// warm-up) with that of the last, private bytes or the managed heap growing by more than half
    /// and by more than 100 MB, or handles by more than half and by more than 500, is a leak; so is a
    /// failed cycle. Medians, so one busy moment doesn't count.
    /// </summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<SoakSample> samples)
    {
        var problems = samples.Where(s => s.Failure is not null).Select(s => $"Cycle {s.Cycle} failed: {s.Failure}").ToList();
        var settled = samples.Where(s => s.Cycle >= WarmUpCycles && s.Failure is null).ToList();
        if (settled.Count < 8)
            return problems;
        int quarter = settled.Count / 4;
        var first = settled.Take(quarter).ToList();
        var last = settled.Skip(settled.Count - quarter).ToList();
        Check("Private bytes", first.Select(s => s.PrivateBytes), last.Select(s => s.PrivateBytes), 100L * 1024 * 1024);
        Check("Managed heap", first.Select(s => s.ManagedHeap), last.Select(s => s.ManagedHeap), 100L * 1024 * 1024);
        Check("Handles", first.Select(s => (long)s.Handles), last.Select(s => (long)s.Handles), 500);
        return problems;

        void Check(string what, IEnumerable<long> before, IEnumerable<long> after, long minimumGrowth)
        {
            long from = Median(before), to = Median(after);
            if (to - from > minimumGrowth && to > from * 3 / 2)
                problems.Add($"{what} grew from {from:N0} to {to:N0}");
        }
    }

    private static long Median(IEnumerable<long> values)
    {
        var sorted = values.Order().ToList();
        return sorted[sorted.Count / 2];
    }

    /// <summary>The samples as CSV.</summary>
    public static string Csv(IEnumerable<SoakSample> samples)
    {
        var csv = new StringBuilder("time,cycle,private_bytes,managed_heap,handles,failure\n");
        foreach (var s in samples)
        {
            csv.Append(CultureInfo.InvariantCulture, $"{s.At:O},{s.Cycle},{s.PrivateBytes},{s.ManagedHeap},{s.Handles},")
                .Append('"').Append((s.Failure ?? string.Empty).Replace("\"", "'")).Append("\"\n");
        }
        return csv.ToString();
    }
}

/// <summary>
/// Runs the desktop scenarios again and again on one Vox for a long time
/// (VOX_SOAK_HOURS, default 4), sampling memory and handles after each cycle into a CSV report
/// (VOX_SOAK_REPORT, default soak.csv in the temp folder), and fails on sustained growth.
/// </summary>
public static class SoakRunner
{
    public static TimeSpan Duration =>
        double.TryParse(Environment.GetEnvironmentVariable("VOX_SOAK_HOURS"), NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) && hours > 0
            ? TimeSpan.FromHours(hours)
            : TimeSpan.FromHours(4);

    public static string ReportPath =>
        Environment.GetEnvironmentVariable("VOX_SOAK_REPORT") is { Length: > 0 } path ? path : Path.Combine(Path.GetTempPath(), "soak.csv");

    public static async Task<IReadOnlyList<SoakSample>> RunAsync(TimeSpan duration, string reportPath, Action<string> log)
    {
        var samples = new List<SoakSample>();
        await using var vox = await VoxHarness.StartAsync();
        var context = new ScenarioContext(vox, ScenarioPacers.None, log);
        var cycleScenarios = DesktopScenarios.All;
        var end = DateTimeOffset.UtcNow + duration;
        for (int cycle = 0; DateTimeOffset.UtcNow < end; cycle++)
        {
            string? failure = null;
            foreach (var scenario in cycleScenarios)
            {
                try { await scenario.RunAsync(context); }
                catch (Exception ex) when (ex is ScenarioFailedException or TimeoutException or InvalidOperationException)
                {
                    failure = $"{scenario.Name}: {ex.Message}";
                    break;
                }
            }
            // Only what is still referenced counts
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using var process = Process.GetCurrentProcess();
            var sample = new SoakSample(DateTimeOffset.UtcNow, cycle, process.PrivateMemorySize64, GC.GetTotalMemory(false), process.HandleCount, failure);
            samples.Add(sample);
            log($"Cycle {cycle}: private {sample.PrivateBytes / (1024 * 1024)} MB, heap {sample.ManagedHeap / (1024 * 1024)} MB, {sample.Handles} handles{(failure is null ? "" : ", FAILED: " + failure)}");
            await File.WriteAllTextAsync(reportPath, SoakAnalysis.Csv(samples));
        }
        return samples;
    }
}

/// <summary>A soak test: needs everything an end-to-end test does, and VOX_SOAK=1.</summary>
public sealed class SoakFactAttribute : FactAttribute
{
    public SoakFactAttribute() =>
        Skip = E2EFactAttribute.SkipReason()
            ?? (Environment.GetEnvironmentVariable("VOX_SOAK") != "1" ? "The soak test runs for hours; set VOX_SOAK=1 to run it (docs/e2e.md)" : null);
}

public class SoakTests(ITestOutputHelper output)
{
    [SoakFact]
    public async Task MemoryAndHandles_StayFlat()
    {
        var samples = await SoakRunner.RunAsync(SoakRunner.Duration, SoakRunner.ReportPath, output.WriteLine);

        var problems = SoakAnalysis.Problems(samples);
        Assert.True(problems.Count == 0, string.Join("; ", problems) + $" (report: {SoakRunner.ReportPath})");
    }
}

/// <summary>The soak analysis itself (an ordinary unit test).</summary>
public class SoakAnalysisTests
{
    private static List<SoakSample> Samples(Func<int, long> privateBytes, int count = 40, Func<int, int>? handles = null) =>
        Enumerable.Range(0, count)
            .Select(i => new SoakSample(DateTimeOffset.UnixEpoch.AddMinutes(i), i, privateBytes(i), 50_000_000, handles?.Invoke(i) ?? 1000))
            .ToList();

    [Fact]
    public void Flat_HasNoProblems() =>
        Assert.Empty(SoakAnalysis.Problems(Samples(i => 300_000_000 + (i % 3) * 5_000_000)));

    [Fact]
    public void SteadyGrowth_IsALeak() =>
        Assert.Contains(SoakAnalysis.Problems(Samples(i => 200_000_000 + i * 20_000_000L)), p => p.StartsWith("Private bytes", StringComparison.Ordinal));

    [Fact]
    public void GrowingHandles_AreALeak() =>
        Assert.Contains(SoakAnalysis.Problems(Samples(_ => 300_000_000, handles: i => 1000 + i * 100)), p => p.StartsWith("Handles", StringComparison.Ordinal));

    [Fact]
    public void WarmUp_AndOneSpike_DontCount()
    {
        var samples = Samples(i => i < SoakAnalysis.WarmUpCycles ? 100_000_000 : i == 30 ? 900_000_000 : 300_000_000);

        Assert.Empty(SoakAnalysis.Problems(samples));
    }

    [Fact]
    public void FailedCycle_IsAProblem_AndTheCsvQuotesIt()
    {
        var samples = Samples(_ => 300_000_000, count: 2);
        samples[1] = samples[1] with { Failure = "notepad: Vox never said \"x\"" };

        Assert.Equal(["Cycle 1 failed: notepad: Vox never said \"x\""], SoakAnalysis.Problems(samples));
        Assert.Contains("\"notepad: Vox never said 'x'\"", SoakAnalysis.Csv(samples));
    }
}
