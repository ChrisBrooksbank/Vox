namespace Vox.Core.AddOns;

/// <summary>
/// Looks at the running add-ons' guards every <see cref="Interval"/> from a timer of its own, so an
/// add-on call that hangs the pipeline or speech thread is still noticed
/// (<see cref="AddOnGuard.CheckForHang"/>): the add-on is stopped and its guard has the manager
/// turn it off. The timer runs only while there are guards to watch.
/// </summary>
public sealed class AddOnWatchdog : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private readonly List<AddOnGuard> _guards = [];
    private ITimer? _timer;

    public AddOnWatchdog(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Guards watched now.</summary>
    public int Count
    {
        get { lock (_lock) return _guards.Count; }
    }

    public void Watch(AddOnGuard guard)
    {
        lock (_lock)
        {
            if (_guards.Contains(guard))
                return;
            _guards.Add(guard);
            _timer ??= _time.CreateTimer(_ => Check(), null, Interval, Interval);
        }
    }

    public void Forget(AddOnGuard guard)
    {
        lock (_lock)
        {
            _guards.Remove(guard);
            if (_guards.Count == 0)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }
    }

    /// <summary>Checks every watched guard for a call that has hung.</summary>
    public void Check()
    {
        AddOnGuard[] guards;
        lock (_lock) guards = [.. _guards];
        foreach (var guard in guards)
            guard.CheckForHang();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _guards.Clear();
            _timer?.Dispose();
            _timer = null;
        }
    }
}
