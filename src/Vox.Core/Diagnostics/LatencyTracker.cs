using Microsoft.Extensions.Logging;

namespace Vox.Core.Diagnostics;

/// <summary>
/// Collects latency samples per stage of the key-to-speech path and reports percentiles:
/// <see cref="HookToDispatcher"/>, <see cref="CommandToPipeline"/>, <see cref="QueueToEngine"/>
/// and the end-to-end <see cref="KeyToSpeech"/> (a key press to the next utterance that starts
/// within a second). Keeps the most recent samples per stage. Thread-safe.
/// </summary>
public sealed class LatencyTracker : IDisposable
{
    public const string HookToDispatcher = "hook to dispatcher";
    public const string CommandToPipeline = "command to pipeline";
    public const string QueueToEngine = "queue to engine";
    public const string KeyToSpeech = "key to speech";

    private static readonly TimeSpan KeyToSpeechWindow = TimeSpan.FromSeconds(1);

    private readonly object _lock = new();
    private readonly Dictionary<string, Queue<double>> _samples = new();
    private readonly int _capacity;
    private readonly Func<long> _clockMs;
    private long? _lastKeyPressMs;
    private System.Threading.Timer? _reportTimer;

    public LatencyTracker(int capacity = 1000, Func<long>? clockMs = null)
    {
        _capacity = capacity;
        _clockMs = clockMs ?? (() => Environment.TickCount64);
    }

    public void Record(string stage, TimeSpan latency)
    {
        lock (_lock)
        {
            if (!_samples.TryGetValue(stage, out var queue))
                _samples[stage] = queue = new Queue<double>();
            queue.Enqueue(latency.TotalMilliseconds);
            while (queue.Count > _capacity)
                queue.Dequeue();
        }
    }

    /// <summary>A key went down (the start of a key-to-speech measurement).</summary>
    public void NoteKeyPress()
    {
        lock (_lock) _lastKeyPressMs = _clockMs();
    }

    /// <summary>Speech started: completes the key-to-speech measurement for the last key, once.</summary>
    public void NoteSpeechStarted()
    {
        long? pressed;
        long now = _clockMs();
        lock (_lock)
        {
            pressed = _lastKeyPressMs;
            _lastKeyPressMs = null;
        }
        if (pressed is { } at && now - at <= KeyToSpeechWindow.TotalMilliseconds)
            Record(KeyToSpeech, TimeSpan.FromMilliseconds(now - at));
    }

    /// <summary>The <paramref name="percentile"/> (0–1) of a stage's samples in ms, or null without samples.</summary>
    public double? Percentile(string stage, double percentile)
    {
        double[] sorted;
        lock (_lock)
        {
            if (!_samples.TryGetValue(stage, out var queue) || queue.Count == 0)
                return null;
            sorted = queue.ToArray();
        }
        Array.Sort(sorted);
        // Nearest-rank method
        int rank = (int)Math.Ceiling(Math.Clamp(percentile, 0, 1) * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }

    public int Count(string stage)
    {
        lock (_lock) return _samples.TryGetValue(stage, out var q) ? q.Count : 0;
    }

    /// <summary>"key to speech p95 42 ms (n=120); ..." for every stage with samples.</summary>
    public string Summary()
    {
        string[] stages;
        lock (_lock) stages = _samples.Keys.OrderBy(k => k).ToArray();
        return string.Join("; ", stages
            .Select(s => (Stage: s, P95: Percentile(s, 0.95), N: Count(s)))
            .Where(x => x.P95 is not null)
            .Select(x => $"{x.Stage} p95 {x.P95:0} ms (n={x.N})"));
    }

    /// <summary>Logs <see cref="Summary"/> every <paramref name="interval"/> while there are samples.</summary>
    public void StartReporting(ILogger logger, TimeSpan interval)
    {
        _reportTimer ??= new System.Threading.Timer(_ =>
        {
            var summary = Summary();
            if (summary.Length > 0)
                logger.LogInformation("Latency: {Summary}", summary);
        }, null, interval, interval);
    }

    public void Dispose() => _reportTimer?.Dispose();
}
