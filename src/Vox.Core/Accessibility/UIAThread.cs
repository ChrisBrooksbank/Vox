using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Vox.Core.Accessibility;

/// <summary>
/// Provides a dedicated STA thread for all UIA COM operations.
/// All UIA calls must be marshaled through this class to avoid cross-thread COM violations.
/// </summary>
public sealed class UIAThread : IDisposable
{
    private readonly Thread _thread;
    private readonly BlockingCollection<Action> _workQueue = new();
    private readonly ILogger<UIAThread> _logger;
    private bool _disposed;

    public UIAThread(ILogger<UIAThread> logger)
    {
        _logger = logger;
        _thread = new Thread(ThreadProc)
        {
            Name = "Vox-UIA-STA",
            IsBackground = true,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>
    /// Marshals a function call to the dedicated STA thread and returns its result.
    /// </summary>
    public Task<T> RunAsync<T>(Func<T> func)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(UIAThread));

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _workQueue.Add(() =>
        {
            try
            {
                var result = func();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    /// <summary>
    /// Marshals an action to the dedicated STA thread.
    /// </summary>
    public Task RunAsync(Action action)
    {
        return RunAsync<bool>(() => { action(); return true; });
    }

    private void ThreadProc()
    {
        _logger.LogDebug("UIA STA thread started (apartment: {State})", _thread.GetApartmentState());

        try
        {
            foreach (var work in _workQueue.GetConsumingEnumerable())
            {
                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    // Exceptions are propagated via TaskCompletionSource; log unexpected ones
                    _logger.LogError(ex, "Unexpected exception in UIA STA thread work item");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }

        _logger.LogDebug("UIA STA thread exiting");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _workQueue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(5));
        _workQueue.Dispose();
    }
}
