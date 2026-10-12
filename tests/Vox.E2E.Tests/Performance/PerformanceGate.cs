using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vox.E2E.Tests.Performance;

/// <summary>What one page of a performance run measured: its time to first speech and each key's latency (milliseconds).</summary>
public sealed record PagePerformance(string Page, double TimeToFirstSpeechMs, IReadOnlyList<double> KeyLatenciesMs);

/// <summary>
/// A performance run (docs/performance.md): every page's figures, the 95th-percentile keypress
/// latency over all of them, and where it ran.
/// </summary>
public sealed record PerformanceReport(
    DateTimeOffset At,
    string Machine,
    string OperatingSystem,
    string Browser,
    string VoxVersion,
    IReadOnlyList<PagePerformance> Pages)
{
    /// <summary>The 95th percentile of every key's latency on every page.</summary>
    public double? KeypressP95Ms => Percentile.Of(Pages.SelectMany(p => p.KeyLatenciesMs).ToList(), 95);

    public string ToJson() => JsonSerializer.Serialize(this, PerformanceJson.Options);

    /// <summary>The figures as a few lines of text, for the test log.</summary>
    public string Summary()
    {
        var lines = new List<string>
        {
            $"Keypress latency p95: {Format(KeypressP95Ms)} ({Pages.Sum(p => p.KeyLatenciesMs.Count)} keys)",
        };
        foreach (var page in Pages)
            lines.Add($"{page.Page}: first speech {page.TimeToFirstSpeechMs:0} ms, key p95 {Format(Percentile.Of(page.KeyLatenciesMs, 95))}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string Format(double? ms) => ms is { } value ? $"{value:0} ms" : "none";
}

/// <summary>Percentiles by the nearest-rank method (the value at rank ⌈p/100 × n⌉ of the sorted samples).</summary>
public static class Percentile
{
    /// <summary>The <paramref name="percent"/>th percentile of <paramref name="samples"/>, or null when there are none.</summary>
    public static double? Of(IReadOnlyCollection<double> samples, double percent)
    {
        if (samples.Count == 0)
            return null;
        if (percent is <= 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percent));
        var sorted = samples.Order().ToList();
        int rank = (int)Math.Ceiling(percent / 100 * sorted.Count);
        return sorted[Math.Clamp(rank, 1, sorted.Count) - 1];
    }
}

/// <summary>
/// The figures a run is held to, and how much worse it may be: a figure regresses when it is more
/// than <see cref="MarginPercent"/> higher than its baseline and also more than the absolute margin
/// higher (so a few milliseconds of noise on a small figure never fail the run). A figure with no
/// baseline is reported, never failed. Kept in <c>tests/Vox.E2E.Tests/Performance/baseline.json</c>.
/// </summary>
public sealed record PerformanceBaseline
{
    /// <summary>The 95th-percentile keypress latency to hold to (ms), or null before the first recorded run.</summary>
    public double? KeypressP95Ms { get; init; }

    /// <summary>Each page's time to first speech (ms), by page file.</summary>
    public Dictionary<string, double> TimeToFirstSpeechMs { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How much higher than its baseline a figure may be, in percent.</summary>
    public double MarginPercent { get; init; } = 25;

    /// <summary>... and in milliseconds, for keypress latency.</summary>
    public double KeypressMarginMs { get; init; } = 30;

    /// <summary>... and in milliseconds, for time to first speech.</summary>
    public double FirstSpeechMarginMs { get; init; } = 300;

    /// <summary>Where the figures came from (machine, browser, date), for people reading the file.</summary>
    public string? RecordedOn { get; init; }

    public static PerformanceBaseline Parse(string json) =>
        JsonSerializer.Deserialize<PerformanceBaseline>(json, PerformanceJson.Options) ?? new PerformanceBaseline();

    public static PerformanceBaseline Load(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path)) : new PerformanceBaseline();

    public string ToJson() => JsonSerializer.Serialize(this, PerformanceJson.Options);

    /// <summary>A baseline holding <paramref name="report"/>'s figures, with these margins.</summary>
    public PerformanceBaseline From(PerformanceReport report) => this with
    {
        KeypressP95Ms = report.KeypressP95Ms is { } p95 ? Math.Round(p95) : KeypressP95Ms,
        TimeToFirstSpeechMs = report.Pages.ToDictionary(p => p.Page, p => Math.Round(p.TimeToFirstSpeechMs), StringComparer.OrdinalIgnoreCase),
        RecordedOn = $"{report.Machine}, {report.OperatingSystem}, {report.Browser}, Vox {report.VoxVersion}, {report.At:yyyy-MM-dd}",
    };
}

/// <summary>One figure worse than its baseline beyond the margins.</summary>
public sealed record Regression(string Figure, double BaselineMs, double MeasuredMs, double AllowedMs)
{
    public override string ToString() =>
        $"{Figure}: {MeasuredMs:0} ms, baseline {BaselineMs:0} ms, at most {AllowedMs:0} ms allowed";
}

/// <summary>Holds a run's figures to the baseline (the CI regression gate).</summary>
public static class PerformanceGate
{
    /// <summary>The figures of <paramref name="report"/> worse than <paramref name="baseline"/> beyond its margins.</summary>
    public static IReadOnlyList<Regression> Check(PerformanceReport report, PerformanceBaseline baseline)
    {
        var regressions = new List<Regression>();
        if (baseline.KeypressP95Ms is { } latency && report.KeypressP95Ms is { } measured)
            Hold("Keypress latency p95", latency, measured, baseline.MarginPercent, baseline.KeypressMarginMs, regressions);
        foreach (var page in report.Pages)
        {
            if (baseline.TimeToFirstSpeechMs.TryGetValue(page.Page, out var firstSpeech))
                Hold($"Time to first speech, {page.Page}", firstSpeech, page.TimeToFirstSpeechMs, baseline.MarginPercent,
                    baseline.FirstSpeechMarginMs, regressions);
        }
        return regressions;
    }

    /// <summary>The highest a figure with <paramref name="baseline"/> may be.</summary>
    public static double Allowed(double baseline, double marginPercent, double marginMs) =>
        Math.Max(baseline * (1 + marginPercent / 100), baseline + marginMs);

    private static void Hold(string figure, double baseline, double measured, double marginPercent, double marginMs, List<Regression> regressions)
    {
        var allowed = Allowed(baseline, marginPercent, marginMs);
        if (measured > allowed)
            regressions.Add(new Regression(figure, baseline, measured, allowed));
    }
}

internal static class PerformanceJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
