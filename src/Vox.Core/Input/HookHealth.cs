namespace Vox.Core.Input;

/// <summary>What <see cref="HookHealth.Check"/> asks the keyboard hook to do.</summary>
public enum HookHealthAction
{
    None,
    /// <summary>Inject a probe key the hook swallows: if the hook is alive it sees it.</summary>
    Probe,
    /// <summary>The probe went unseen: Windows removed the hook; install it again.</summary>
    Reinstall,
}

/// <summary>
/// Notices a low-level keyboard hook Windows removed without telling anyone (a callback that once
/// took longer than <c>LowLevelHooksTimeout</c>). Windows says when the user last gave input
/// (<c>GetLastInputInfo</c>, keyboard or mouse); when there has been input the hook didn't see for
/// a while, one probe key is injected, and if the hook doesn't see that either it is reinstalled.
/// Probes are rate-limited, so a mouse user who isn't typing gets at most one per
/// <see cref="ProbeInterval"/>, and none while the computer is idle (a probe would reset the idle
/// timer). Times are milliseconds on one clock (<c>Environment.TickCount64</c>); thread-safe:
/// <see cref="KeySeen"/> is called on the hook thread, <see cref="Check"/> on a timer.
/// </summary>
public sealed class HookHealth
{
    /// <summary>How long the hook may see nothing while there is input before it is probed.</summary>
    public static readonly TimeSpan Silence = TimeSpan.FromSeconds(5);
    /// <summary>How long a probe has to reach the hook.</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(1);
    /// <summary>The shortest time between probes.</summary>
    public static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(30);

    private readonly object _lock = new();
    private long _lastSeen;
    private long? _probeSent;
    private long _lastProbe;

    /// <param name="now">When the hook started (it has seen nothing yet).</param>
    public HookHealth(long now)
    {
        _lastSeen = now;
        _lastProbe = now - (long)ProbeInterval.TotalMilliseconds;
    }

    /// <summary>The hook saw a key (any key, the probe included). Called from the hook callback: cheap.</summary>
    public void KeySeen(long now) => Interlocked.Exchange(ref _lastSeen, now);

    /// <summary>
    /// What to do now, given when Windows last had input (<paramref name="lastInput"/>, the same clock).
    /// After <see cref="HookHealthAction.Reinstall"/> the hook counts as new.
    /// </summary>
    public HookHealthAction Check(long now, long lastInput)
    {
        lock (_lock)
        {
            long lastSeen = Interlocked.Read(ref _lastSeen);
            if (_probeSent is { } sent)
            {
                if (lastSeen >= sent)
                {
                    _probeSent = null;
                    return HookHealthAction.None;
                }
                if (now - sent < (long)ProbeTimeout.TotalMilliseconds)
                    return HookHealthAction.None;
                _probeSent = null;
                Interlocked.Exchange(ref _lastSeen, now);
                return HookHealthAction.Reinstall;
            }

            // Nothing happened the hook didn't see, or it saw something recently
            if (lastInput <= lastSeen || now - lastSeen < (long)Silence.TotalMilliseconds)
                return HookHealthAction.None;
            if (now - _lastProbe < (long)ProbeInterval.TotalMilliseconds)
                return HookHealthAction.None;
            _probeSent = now;
            _lastProbe = now;
            return HookHealthAction.Probe;
        }
    }
}
