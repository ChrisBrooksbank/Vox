using Microsoft.Extensions.Logging;

namespace Vox.Core.AddOns;

/// <summary>
/// Checks that an unloaded add-on's code really leaves memory: <see cref="Delay"/> after it was
/// unloaded (so work already posted has run), off the pipeline thread, collects garbage until its
/// load context is gone, and logs "did not unload" when something still holds on to it (an
/// add-on's thread, a static event in Vox it subscribed to, a timer it never stopped). Such an
/// add-on keeps working memory until Vox restarts, though it no longer does anything.
/// </summary>
public sealed class AddOnUnloadVerifier
{
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(2);

    /// <summary>Collections tried before the context is taken to be held on to.</summary>
    public const int MaxCollections = 10;

    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly HashSet<ITimer> _pending = [];
    private readonly object _lock = new();

    public AddOnUnloadVerifier(ILogger logger, TimeProvider? time = null)
    {
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised after each check with the add-on's id and whether its code was let go.</summary>
    public event Action<string, bool>? Verified;

    /// <summary>Checks after <see cref="Delay"/> that <paramref name="loadContext"/> has been collected.</summary>
    public void Track(string addOnId, WeakReference loadContext)
    {
        lock (_lock)
        {
            ITimer? timer = null;
            timer = _time.CreateTimer(_ =>
            {
                lock (_lock)
                {
                    // Kept until it fires: an unreferenced timer could itself be collected
                    if (timer is null || !_pending.Remove(timer))
                        return;
                }
                timer.Dispose();
                Verify(addOnId, loadContext);
            }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _pending.Add(timer);
            timer.Change(Delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Collects until <paramref name="loadContext"/> is gone; false (and logged) when it survives.</summary>
    public bool Verify(string addOnId, WeakReference loadContext)
    {
        for (int i = 0; i < MaxCollections && loadContext.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        var unloaded = !loadContext.IsAlive;
        if (unloaded)
            _logger.LogDebug("Add-on {AddOn} unloaded from memory", addOnId);
        else
            _logger.LogWarning("Add-on {AddOn} did not unload: something still refers to its code, which stays in memory until Vox restarts", addOnId);
        try { Verified?.Invoke(addOnId, unloaded); }
        catch (Exception ex) { _logger.LogError(ex, "Unload check handler failed"); }
        return unloaded;
    }
}
