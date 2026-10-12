using Microsoft.Extensions.Logging;

namespace Vox.Core.AddOns;

/// <summary>
/// Every call Vox makes into one add-on goes through its guard: an exception is caught and logged
/// and the call's fallback used, so a broken add-on can't take Vox down or silence it. An add-on
/// that throws <see cref="MaxFailures"/> times within <see cref="Window"/> is stopped: from then on
/// its calls are skipped, and <c>onRepeatedFailure</c> (once) has the manager turn it off and tell
/// the user. Thread-safe: filters are called on the speech thread, the rest on the pipeline thread.
/// </summary>
public sealed class AddOnGuard
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly ILogger _logger;
    private readonly Action<AddOnGuard> _onRepeatedFailure;
    private readonly TimeProvider _time;
    private readonly Queue<DateTimeOffset> _failures = new();
    private readonly object _lock = new();
    private volatile bool _stopped;

    public AddOnGuard(string addOnId, ILogger logger, Action<AddOnGuard> onRepeatedFailure, TimeProvider? time = null)
    {
        AddOnId = addOnId;
        _logger = logger;
        _onRepeatedFailure = onRepeatedFailure;
        _time = time ?? TimeProvider.System;
    }

    public string AddOnId { get; }

    /// <summary>The add-on failed too often (or was unloaded): its calls are skipped.</summary>
    public bool IsStopped => _stopped;

    /// <summary>Failures so far (within the window).</summary>
    public int RecentFailures
    {
        get { lock (_lock) return _failures.Count; }
    }

    /// <summary>Skips every call from now on (the add-on is being unloaded).</summary>
    public void Stop() => _stopped = true;

    /// <summary>Runs <paramref name="action"/> unless stopped; false when it was skipped or threw.</summary>
    public bool Run(Action action, string what)
    {
        if (_stopped)
            return false;
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            Failed(ex, what);
            return false;
        }
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

    /// <summary>The result of <paramref name="call"/>, or <paramref name="fallback"/> when stopped or when it threw.</summary>
    public T Call<T>(Func<T> call, T fallback, string what)
    {
        if (_stopped)
            return fallback;
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            Failed(ex, what);
            return fallback;
        }
    }

    /// <summary>Records a failure that happened outside <see cref="Run"/> (an engine's task that faulted).</summary>
    public void Failed(Exception ex, string what)
    {
        // What failed, never the text it was given
        _logger.LogError(ex, "Add-on {AddOn} failed {What}", AddOnId, what);
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
            try { _onRepeatedFailure(this); }
            catch (Exception callbackError) { _logger.LogError(callbackError, "Could not turn off add-on {AddOn}", AddOnId); }
        }
    }
}
