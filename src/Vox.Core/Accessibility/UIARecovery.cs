using Microsoft.Extensions.Logging;

namespace Vox.Core.Accessibility;

/// <summary>
/// Brings UIA back after <see cref="UIAThread.ThreadReplaced"/>: runs the recovery steps in order
/// (new automation object, event subscriptions, document reload). A step that fails is logged and
/// the rest still run; a replacement during recovery starts it again once the current run ends.
/// </summary>
public sealed class UIARecovery : IDisposable
{
    private readonly UIAThread _uiaThread;
    private readonly IReadOnlyList<(string Name, Func<Task> Run)> _steps;
    private readonly ILogger<UIARecovery> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _pending;

    public UIARecovery(UIAThread uiaThread, UIAProvider provider, UIAEventSubscriber subscriber,
        BrowseDocumentTracker tracker, ILogger<UIARecovery> logger)
        : this(uiaThread, logger,
            ("automation object", provider.ReinitializeAsync),
            ("event subscriptions", subscriber.ResubscribeAsync),
            ("document", tracker.ReloadAfterThreadReplacedAsync))
    {
    }

    public UIARecovery(UIAThread uiaThread, ILogger<UIARecovery> logger, params (string Name, Func<Task> Run)[] steps)
    {
        _uiaThread = uiaThread;
        _logger = logger;
        _steps = steps;
        _uiaThread.ThreadReplaced += OnThreadReplaced;
    }

    /// <summary>Raised after each recovery run, with whether every step succeeded.</summary>
    public event EventHandler<bool>? Recovered;

    private void OnThreadReplaced(object? sender, EventArgs e) => _ = RecoverAsync();

    /// <summary>Runs the recovery steps (also called by <see cref="UIAThread.ThreadReplaced"/>).</summary>
    public async Task RecoverAsync()
    {
        Interlocked.Exchange(ref _pending, 1);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            while (Interlocked.Exchange(ref _pending, 0) == 1)
            {
                bool allSucceeded = true;
                foreach (var (name, run) in _steps)
                {
                    try
                    {
                        await run().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        allSucceeded = false;
                        _logger.LogError(ex, "UIA recovery: could not restore the {Step}", name);
                    }
                }
                _logger.LogInformation("UIA recovery finished (all steps succeeded: {Succeeded})", allSucceeded);
                Recovered?.Invoke(this, allSucceeded);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _uiaThread.ThreadReplaced -= OnThreadReplaced;
    }
}
