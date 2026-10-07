using Vox.Core.Configuration;
using Vox.Core.Pipeline;

namespace Vox.Core.Navigation;

/// <summary>What to do about a progress change: speak a percentage, or beep at a pitch.</summary>
public readonly record struct ProgressReport(string? Speech, double? ToneHz);

/// <summary>
/// Decides how progress bar changes are reported (<see cref="VoxSettings.ProgressBars"/>): the
/// percentage each time it crosses a 10 % or 25 % step, or a short beep that rises in pitch
/// (110 Hz at 0 % to 1760 Hz at 100 %, at most every 100 ms). Only the foreground application's
/// progress bars are reported unless <see cref="VoxSettings.ReportBackgroundProgress"/> is on.
/// Each progress bar is followed separately. Call on the pipeline thread.
/// </summary>
public sealed class ProgressReporter
{
    private static readonly TimeSpan BeepInterval = TimeSpan.FromMilliseconds(100);

    private readonly Func<VoxSettings> _settings;
    private readonly Func<int?> _foregroundProcessId;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, (int Step, int Percent, DateTimeOffset At)> _last = new();

    public ProgressReporter(Func<VoxSettings> settings, Func<int?> foregroundProcessId, Func<DateTimeOffset>? clock = null)
    {
        _settings = settings;
        _foregroundProcessId = foregroundProcessId;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The report for this change, or null to stay quiet.</summary>
    public ProgressReport? Evaluate(ProgressChangedEvent e)
    {
        var settings = _settings();
        if (settings.ProgressBars == ProgressReporting.Off)
            return null;
        if (!settings.ReportBackgroundProgress && _foregroundProcessId() is { } foreground && foreground != e.ProcessId)
            return null;

        int percent = Percent(e.Value, e.Minimum, e.Maximum);
        var key = string.Join(",", e.RuntimeId);
        var now = _clock();
        bool known = _last.TryGetValue(key, out var last);

        if (settings.ProgressBars == ProgressReporting.Beep)
        {
            if (known && (percent == last.Percent || now - last.At < BeepInterval))
                return null;
            _last[key] = (0, percent, now);
            return new ProgressReport(null, ToneFor(percent));
        }

        int stepSize = settings.ProgressBars == ProgressReporting.Every25Percent ? 25 : 10;
        int step = percent / stepSize;
        // Reported when the step changes; a bar first seen part-way is reported straight away
        if (known && step == last.Step)
            return null;
        _last[key] = (step, percent, now);
        return new ProgressReport($"{percent} percent", null);
    }

    /// <summary>The beep pitch for a percentage: 110 Hz at 0 % to 1760 Hz at 100 %, rising evenly by octaves.</summary>
    public static double ToneFor(int percent) => 110 * Math.Pow(2, Math.Clamp(percent, 0, 100) / 25.0);

    public static int Percent(double value, double minimum, double maximum)
    {
        if (maximum <= minimum)
            return (int)Math.Clamp(Math.Round(value), 0, 100);
        return (int)Math.Clamp(Math.Round((value - minimum) / (maximum - minimum) * 100), 0, 100);
    }

    /// <summary>Forgets every progress bar (e.g. on settings change).</summary>
    public void Reset() => _last.Clear();
}
