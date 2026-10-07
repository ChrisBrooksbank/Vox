using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Vox.Core.Accessibility;

/// <summary>
/// Provides a dedicated STA thread for all UIA COM operations.
/// All UIA calls must be marshaled through this class to avoid cross-thread COM violations.
/// </summary>
/// <remarks>
/// Every call has a timeout, so one unresponsive provider can't make its callers (and through them
/// the whole screen reader) wait forever. A call that times out is abandoned: its caller gets a
/// <see cref="UIATimeoutException"/>, a call still queued when its time is up is skipped, and the
/// result of one already running is discarded when it finally returns.
/// <para>
/// If a call never returns at all, <see cref="UIAWatchdog"/> calls <see cref="ReplaceStuckThread"/>:
/// later calls (and those still queued) run on a new STA thread, and the stuck one is left to
/// finish or die on its own. <see cref="ThreadReplaced"/> tells the owners of COM objects created
/// on the old thread to create new ones.
/// </para>
/// </remarks>
public sealed class UIAThread : IDisposable
{
    /// <summary>Timeout for ordinary UIA calls (property reads, navigation, patterns).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Timeout for subscribing to and unsubscribing from events, and creating the automation object.</summary>
    public static readonly TimeSpan SetupTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Timeout for calls that capture or search a whole document, which UIA itself allows
    /// <see cref="UIAProvider.DocumentCaptureTimeoutMs"/>; a little longer so UIA's own timeout fires first.
    /// </summary>
    public static readonly TimeSpan DocumentTimeout = TimeSpan.FromMilliseconds(UIAProvider.DocumentCaptureTimeoutMs + 5000);

    private readonly ILogger<UIAThread> _logger;
    private readonly string _name;
    private readonly object _workerLock = new();
    private Worker _worker;
    private int _generation;
    private bool _disposed;

    public UIAThread(ILogger<UIAThread> logger) : this(logger, "Vox-UIA-STA")
    {
    }

    internal UIAThread(ILogger<UIAThread> logger, string name)
    {
        _logger = logger;
        _name = name;
        _worker = new Worker(this, $"{name}-0");
    }

    /// <summary>
    /// Raised (on a thread-pool thread) after a stuck STA thread has been replaced. COM objects
    /// created on the old thread must be re-created by a call on the new one.
    /// </summary>
    public event EventHandler? ThreadReplaced;

    /// <summary>How many times the STA thread has been replaced.</summary>
    public int Generation => Volatile.Read(ref _generation);

    /// <summary>The managed thread id of the current STA thread.</summary>
    public int ManagedThreadId => CurrentWorker.ManagedThreadId;

    /// <summary>
    /// How long the work item now running has been running, or null when the thread is idle.
    /// </summary>
    public TimeSpan? CurrentWorkDuration => CurrentWorker.CurrentWorkDuration;

    /// <summary>The timeout of the work item now running, or null when the thread is idle.</summary>
    public TimeSpan? CurrentWorkTimeout => CurrentWorker.CurrentWorkTimeout;

    private Worker CurrentWorker
    {
        get { lock (_workerLock) return _worker; }
    }

    /// <summary>
    /// Marshals a function call to the dedicated STA thread and returns its result. Fails with
    /// <see cref="UIATimeoutException"/> if the call hasn't finished within <paramref name="timeout"/>
    /// (default <see cref="DefaultTimeout"/>), counted from now, so time spent queued counts too.
    /// </summary>
    public Task<T> RunAsync<T>(Func<T> func, TimeSpan? timeout = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(UIAThread));

        var limit = timeout ?? DefaultTimeout;
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutCts = limit == Timeout.InfiniteTimeSpan ? null : new CancellationTokenSource(limit);
        var registration = timeoutCts?.Token.Register(() =>
        {
            if (tcs.TrySetException(new UIATimeoutException(limit)))
                _logger.LogWarning("UIA call abandoned after {Timeout} ms", (int)limit.TotalMilliseconds);
        });

        var work = new WorkItem(
            run: () =>
            {
                try
                {
                    tcs.TrySetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            },
            isAbandoned: () => tcs.Task.IsCompleted,
            timeout: limit);

        // Release the timer once the call is settled either way
        tcs.Task.ContinueWith(_ =>
        {
            registration?.Dispose();
            timeoutCts?.Dispose();
        }, TaskScheduler.Default);

        bool added;
        lock (_workerLock)
            added = !_disposed && _worker.TryAdd(work);
        if (!added)
        {
            // Dispose() completed the queue concurrently with this call
            timeoutCts?.Cancel(throwOnFirstException: false);
            throw new ObjectDisposedException(nameof(UIAThread));
        }
        return tcs.Task;
    }

    /// <summary>
    /// Marshals an action to the dedicated STA thread (see <see cref="RunAsync{T}(Func{T}, TimeSpan?)"/>).
    /// </summary>
    public Task RunAsync(Action action, TimeSpan? timeout = null)
    {
        return RunAsync<bool>(() => { action(); return true; }, timeout);
    }

    /// <summary>
    /// Starts a new STA thread for all later work, moves the work still queued onto it, and leaves
    /// the current thread (stuck in a call) to finish on its own. Raises <see cref="ThreadReplaced"/>.
    /// </summary>
    public void ReplaceStuckThread()
    {
        Worker old;
        lock (_workerLock)
        {
            if (_disposed) return;
            old = _worker;
            var generation = Interlocked.Increment(ref _generation);
            _worker = new Worker(this, $"{_name}-{generation}");
            foreach (var queued in old.CompleteAndDrain())
                _worker.TryAdd(queued);
        }
        // The old queue isn't disposed: the stuck thread may still be enumerating it when (if ever)
        // its call returns, and it then exits because the queue is completed
        _logger.LogWarning("UIA thread {Old} was stuck; replaced it with {New}", old.Name, CurrentWorker.Name);

        var handler = ThreadReplaced;
        if (handler is not null)
            Task.Run(() =>
            {
                try { handler(this, EventArgs.Empty); }
                catch (Exception ex) { _logger.LogError(ex, "Error in ThreadReplaced handler"); }
            });
    }

    public void Dispose() => Dispose(TimeSpan.FromSeconds(5));

    /// <summary>
    /// Stops accepting work and waits up to <paramref name="joinTimeout"/> for the thread to finish.
    /// A thread stuck in a call is left to finish on its own (it is a background thread).
    /// </summary>
    internal void Dispose(TimeSpan joinTimeout)
    {
        Worker worker;
        lock (_workerLock)
        {
            if (_disposed) return;
            _disposed = true;
            worker = _worker;
        }
        worker.Shutdown(joinTimeout);
    }

    private sealed class WorkItem(Action run, Func<bool> isAbandoned, TimeSpan timeout)
    {
        public Action Run { get; } = run;
        public Func<bool> IsAbandoned { get; } = isAbandoned;
        public TimeSpan Timeout { get; } = timeout;
    }

    /// <summary>One STA thread and its work queue.</summary>
    private sealed class Worker
    {
        private readonly UIAThread _owner;
        private readonly Thread _thread;
        private readonly BlockingCollection<WorkItem> _queue = new();
        private long _currentWorkStartedTicks; // 0 when idle; Environment.TickCount64 when a work item started
        private WorkItem? _current;

        public Worker(UIAThread owner, string name)
        {
            _owner = owner;
            Name = name;
            _thread = new Thread(ThreadProc) { Name = name, IsBackground = true };
            // COM apartments only exist on Windows (unit tests of the queueing logic also run elsewhere)
            if (OperatingSystem.IsWindows())
                _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public string Name { get; }

        public int ManagedThreadId => _thread.ManagedThreadId;

        public TimeSpan? CurrentWorkDuration
        {
            get
            {
                var started = Interlocked.Read(ref _currentWorkStartedTicks);
                return started == 0 ? null : TimeSpan.FromMilliseconds(Environment.TickCount64 - started);
            }
        }

        public TimeSpan? CurrentWorkTimeout => Volatile.Read(ref _current)?.Timeout;

        public bool TryAdd(WorkItem work)
        {
            try
            {
                _queue.Add(work);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false; // completed for adding
            }
        }

        /// <summary>Stops accepting work and returns what was still queued.</summary>
        public List<WorkItem> CompleteAndDrain()
        {
            _queue.CompleteAdding();
            var drained = new List<WorkItem>();
            while (_queue.TryTake(out var work))
                drained.Add(work);
            return drained;
        }

        public void Shutdown(TimeSpan joinTimeout)
        {
            _queue.CompleteAdding();
            // A work item can outlast the wait (a large page's capture may take up to 20 s). The
            // queue must outlive the thread then: disposing it under the running enumerator would
            // throw on the STA thread, and an unhandled exception there takes the process down
            if (_thread.Join(joinTimeout))
                _queue.Dispose();
        }

        private void ThreadProc()
        {
            var logger = _owner._logger;
            logger.LogDebug("UIA STA thread {Name} started", Name);

            try
            {
                foreach (var work in _queue.GetConsumingEnumerable())
                {
                    // Its caller has already given up on it (timed out while queued): don't start it
                    if (work.IsAbandoned())
                        continue;

                    Volatile.Write(ref _current, work);
                    Interlocked.Exchange(ref _currentWorkStartedTicks, Math.Max(1, Environment.TickCount64));
                    try
                    {
                        work.Run();
                    }
                    catch (Exception ex)
                    {
                        // Exceptions are propagated via TaskCompletionSource; log unexpected ones
                        logger.LogError(ex, "Unexpected exception in UIA STA thread work item");
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _currentWorkStartedTicks, 0);
                        Volatile.Write(ref _current, null);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (ObjectDisposedException)
            {
                // Queue disposed while shutting down
            }

            logger.LogDebug("UIA STA thread {Name} exiting", Name);
        }
    }
}

/// <summary>
/// A UIA call didn't finish within its timeout (the provider, usually another app, isn't responding).
/// </summary>
public sealed class UIATimeoutException : TimeoutException
{
    public UIATimeoutException(TimeSpan timeout)
        : base($"UIA call did not complete within {(int)timeout.TotalMilliseconds} ms")
    {
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }
}
