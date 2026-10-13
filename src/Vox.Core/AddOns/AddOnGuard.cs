using Microsoft.Extensions.Logging;

namespace Vox.Core.AddOns;

/// <summary>
/// Every call Vox makes into one add-on goes through its guard: an exception is caught and logged
/// and the call's fallback used, so a broken add-on can't take Vox down or silence it. Every call is
/// timed too: one that takes longer than its budget (<see cref="PipelineBudget"/>, or
/// <see cref="FilterBudget"/> for announcement filters on the speech thread) is logged and counted
/// like an exception. An add-on that fails <see cref="MaxFailures"/> times within <see cref="Window"/>
/// is stopped: from then on its calls are skipped, and <c>onRepeatedFailure</c> (once) has the
/// manager turn it off and tell the user. A call still running after <see cref="HangLimit"/> stops
/// the add-on the same way when <see cref="CheckForHang"/> (the <see cref="AddOnWatchdog"/>) finds
/// it (<see cref="HasHung"/>). Thread-safe: filters are called on the speech thread, the rest on the
/// pipeline thread, <see cref="CheckForHang"/> on the watchdog's timer.
/// </summary>
public sealed class AddOnGuard
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>How long a call on the pipeline thread may take before it counts as a failure.</summary>
    public static readonly TimeSpan PipelineBudget = TimeSpan.FromMilliseconds(50);

    /// <summary>How long an announcement filter (speech thread) may take before it counts as a failure.</summary>
    public static readonly TimeSpan FilterBudget = TimeSpan.FromMilliseconds(10);

    /// <summary>How long starting an add-on may take before it counts as a failure.</summary>
    public static readonly TimeSpan StartBudget = TimeSpan.FromMilliseconds(500);

    /// <summary>A call still running this long means the add-on has stopped responding.</summary>
    public static readonly TimeSpan HangLimit = TimeSpan.FromSeconds(2);

    private readonly ILogger _logger;
    private readonly Action<AddOnGuard> _onRepeatedFailure;
    private readonly TimeProvider _time;
    private readonly Queue<DateTimeOffset> _failures = new();
    // The calls running now (more than one when a filter runs on the speech thread meanwhile)
    private readonly Dictionary<long, (DateTimeOffset Started, string What)> _running = [];
    private readonly object _lock = new();
    private long _nextCall;
    private volatile bool _stopped;
    private volatile bool _hung;

    public AddOnGuard(string addOnId, ILogger logger, Action<AddOnGuard> onRepeatedFailure, TimeProvider? time = null)
    {
        AddOnId = addOnId;
        _logger = logger;
        _onRepeatedFailure = onRepeatedFailure;
        _time = time ?? TimeProvider.System;
    }

    public string AddOnId { get; }

    /// <summary>The add-on failed too often, stopped responding or was unloaded: its calls are skipped.</summary>
    public bool IsStopped => _stopped;

    /// <summary>The add-on was stopped because a call was still running after <see cref="HangLimit"/>.</summary>
    public bool HasHung => _hung;

    /// <summary>Failures so far (within the window).</summary>
    public int RecentFailures
    {
        get { lock (_lock) return _failures.Count; }
    }

    /// <summary>Skips every call from now on (the add-on is being unloaded).</summary>
    public void Stop() => _stopped = true;

    /// <summary>
    /// Runs <paramref name="action"/> unless stopped; false when it was skipped, threw, or the add-on
    /// was stopped while it ran. Taking longer than <paramref name="budget"/> (default
    /// <see cref="PipelineBudget"/>) counts as a failure, though the call's work stands.
    /// </summary>
    public bool Run(Action action, string what, TimeSpan? budget = null)
    {
        if (_stopped)
            return false;
        var call = Begin(what);
        try
        {
            action();
        }
        catch (Exception ex)
        {
            End(call);
            Failed(ex, what);
            return false;
        }
        return Finished(call, what, budget ?? PipelineBudget);
    }

    /// <summary>
    /// Runs clean-up (closing its engine or display) even once stopped: an add-on turned off for
    /// failing still lets go of what it holds. A failure is logged, not counted.
    /// </summary>
    public void Cleanup(Action action, string what)
    {
        try { action(); }
        catch (Exception ex) { _logger.LogError(ex, "Add-on {AddOn} failed {What}", AddOnId, what); }
    }

    /// <summary>
    /// The result of <paramref name="call"/>, or <paramref name="fallback"/> when stopped (before or
    /// while it ran) or when it threw. Timed like <see cref="Run"/>.
    /// </summary>
    public T Call<T>(Func<T> call, T fallback, string what, TimeSpan? budget = null)
    {
        if (_stopped)
            return fallback;
        var running = Begin(what);
        T result;
        try
        {
            result = call();
        }
        catch (Exception ex)
        {
            End(running);
            Failed(ex, what);
            return fallback;
        }
        return Finished(running, what, budget ?? PipelineBudget) ? result : fallback;
    }

    /// <summary>Records a failure that happened outside <see cref="Run"/> (an engine's task that faulted).</summary>
    public void Failed(Exception ex, string what)
    {
        // What failed, never the text it was given
        _logger.LogError(ex, "Add-on {AddOn} failed {What}", AddOnId, what);
        Count();
    }

    /// <summary>
    /// Stops the add-on when one of its calls has been running for <see cref="HangLimit"/> or more,
    /// and (once) has the manager turn it off. True when it stopped it now.
    /// </summary>
    public bool CheckForHang()
    {
        string? what = null;
        TimeSpan running = TimeSpan.Zero;
        lock (_lock)
        {
            if (_stopped)
                return false;
            var now = _time.GetUtcNow();
            foreach (var call in _running.Values)
            {
                if (now - call.Started >= HangLimit)
                {
                    what = call.What;
                    running = now - call.Started;
                    break;
                }
            }
            if (what is null)
                return false;
            _hung = true;
            _stopped = true;
        }
        _logger.LogWarning("Add-on {AddOn} has been {What} for {Seconds:0.0} s; it stopped responding, turning it off",
            AddOnId, what, running.TotalSeconds);
        TurnOff();
        return true;
    }

    private long Begin(string what)
    {
        lock (_lock)
        {
            var call = ++_nextCall;
            _running[call] = (_time.GetUtcNow(), what);
            return call;
        }
    }

    private TimeSpan End(long call)
    {
        lock (_lock)
        {
            _running.Remove(call, out var started);
            return _time.GetUtcNow() - started.Started;
        }
    }

    /// <summary>Ends a call that returned: false when the add-on was stopped meanwhile; a slow one counts as a failure.</summary>
    private bool Finished(long call, string what, TimeSpan budget)
    {
        var took = End(call);
        if (_stopped)
            return false;
        if (took > budget)
        {
            _logger.LogWarning("Add-on {AddOn} took {Milliseconds:0} ms {What} (budget {Budget:0} ms)",
                AddOnId, took.TotalMilliseconds, what, budget.TotalMilliseconds);
            Count();
        }
        return true;
    }

    private void Count()
    {
        bool stopNow;
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            _failures.Enqueue(now);
            while (_failures.Count > 0 && now - _failures.Peek() >= Window)
                _failures.Dequeue();
            stopNow = !_stopped && _failures.Count >= MaxFailures;
            if (stopNow)
                _stopped = true;
        }
        if (stopNow)
        {
            _logger.LogWarning("Add-on {AddOn} failed {Count} times within {Window}; turning it off", AddOnId, MaxFailures, Window);
            TurnOff();
        }
    }

    private void TurnOff()
    {
        try { _onRepeatedFailure(this); }
        catch (Exception callbackError) { _logger.LogError(callbackError, "Could not turn off add-on {AddOn}", AddOnId); }
    }
}
