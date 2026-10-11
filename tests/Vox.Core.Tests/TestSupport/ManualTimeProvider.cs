namespace Vox.Core.Tests.TestSupport;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock only moves when a test calls <see cref="Advance"/>,
/// firing the timers that fall due on the way (in due order, on the calling thread; periodic
/// timers are re-armed).
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _lock = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset? start = null)
    {
        _now = start ?? new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock) return _now;
    }

    /// <summary>Timers created, not disposed and still armed.</summary>
    public int ActiveTimers
    {
        get { lock (_lock) return _timers.Count(t => !t.Disposed && t.Due is not null); }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_lock)
            _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock on by <paramref name="by"/>, firing every timer due by then.</summary>
    public void Advance(TimeSpan by)
    {
        DateTimeOffset target;
        lock (_lock)
            target = _now + by;
        while (true)
        {
            ManualTimer? next;
            lock (_lock)
            {
                next = _timers.Where(t => !t.Disposed && t.Due is { } due && due <= target)
                    .OrderBy(t => t.Due).FirstOrDefault();
                if (next is null)
                {
                    _now = target;
                    _timers.RemoveAll(t => t.Disposed);
                    return;
                }
                _now = next.Due!.Value;
                // Periodic timers are re-armed before the callback, one-shot ones disarmed
                next.Due = next.Period is { } period ? _now + period : null;
            }
            next.Fire();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due { get; set; }
        public TimeSpan? Period { get; private set; }
        public bool Disposed { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._lock)
            {
                if (Disposed)
                    return false;
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                Period = period == Timeout.InfiniteTimeSpan || period <= TimeSpan.Zero ? null : period;
                return true;
            }
        }

        public void Dispose()
        {
            lock (owner._lock)
            {
                Disposed = true;
                Due = null;
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
