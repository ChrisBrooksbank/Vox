using Microsoft.Extensions.Logging;

namespace Vox.Core.Accessibility;

/// <summary>
/// Watches the UIA STA thread and replaces it when a call has been running so long that it must
/// be stuck: longer than both its own timeout (its caller has already given up on it) and
/// <see cref="StuckThreshold"/>. Whole-document captures legitimately take up to 20 s, which is
/// why a call's own timeout counts and not only the threshold.
/// </summary>
public sealed class UIAWatchdog : IDisposable
{
    /// <summary>A call running at least this long (and past its own timeout) is considered stuck.</summary>
    public static readonly TimeSpan DefaultStuckThreshold = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

    private readonly UIAThread _uiaThread;
    private readonly ILogger<UIAWatchdog> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly object _checkLock = new();
    private System.Threading.Timer? _timer;

    public UIAWatchdog(UIAThread uiaThread, ILogger<UIAWatchdog> logger)
        : this(uiaThread, logger, DefaultStuckThreshold, DefaultPollInterval)
    {
    }

    public UIAWatchdog(UIAThread uiaThread, ILogger<UIAWatchdog> logger, TimeSpan stuckThreshold, TimeSpan pollInterval)
    {
        _uiaThread = uiaThread;
        _logger = logger;
        StuckThreshold = stuckThreshold;
        _pollInterval = pollInterval;
    }

    public TimeSpan StuckThreshold { get; }

    /// <summary>Starts checking the thread periodically.</summary>
    public void Start()
    {
        _timer ??= new System.Threading.Timer(_ => Check(), null, _pollInterval, _pollInterval);
    }

    /// <summary>
    /// Checks the thread once and replaces it if it is stuck. Returns true if it was replaced.
    /// </summary>
    public bool Check()
    {
        lock (_checkLock)
        {
            if (!IsStuck(_uiaThread.CurrentWorkDuration, _uiaThread.CurrentWorkTimeout, StuckThreshold))
                return false;

            _logger.LogWarning("UIA thread stuck in a call for {Duration} ms; replacing it",
                (int)(_uiaThread.CurrentWorkDuration ?? TimeSpan.Zero).TotalMilliseconds);
            _uiaThread.ReplaceStuckThread();
            return true;
        }
    }

    /// <summary>
    /// Whether a call that has been running for <paramref name="duration"/> with the given
    /// <paramref name="timeout"/> is stuck. Calls without a timeout are never considered stuck.
    /// </summary>
    public static bool IsStuck(TimeSpan? duration, TimeSpan? timeout, TimeSpan threshold)
    {
        if (duration is null || timeout is null || timeout == Timeout.InfiniteTimeSpan)
            return false;
        var limit = timeout.Value > threshold ? timeout.Value : threshold;
        return duration.Value >= limit;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
