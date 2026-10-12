using Vox.Core.Input;
using Xunit;

namespace Vox.Core.Tests.Input;

public class HookHealthTests
{
    private static readonly long Silence = (long)HookHealth.Silence.TotalMilliseconds;
    private static readonly long Timeout = (long)HookHealth.ProbeTimeout.TotalMilliseconds;
    private static readonly long Interval = (long)HookHealth.ProbeInterval.TotalMilliseconds;

    [Fact]
    public void AnIdleComputer_IsNeverProbed()
    {
        var health = new HookHealth(0);

        // No input since the hook started: a probe would only reset the idle timer
        Assert.Equal(HookHealthAction.None, health.Check(10 * Interval, lastInput: 0));
    }

    [Fact]
    public void InputTheHookSaw_NeedsNoProbe()
    {
        var health = new HookHealth(0);
        health.KeySeen(100_000);

        Assert.Equal(HookHealthAction.None, health.Check(100_001, lastInput: 100_000));
    }

    [Fact]
    public void ABriefSilence_IsNotProbed()
    {
        var health = new HookHealth(0);
        health.KeySeen(100_000);

        Assert.Equal(HookHealthAction.None, health.Check(100_000 + Silence - 1, lastInput: 100_000 + Silence - 2));
    }

    [Fact]
    public void InputTheHookMissed_IsProbed_AndAnArrivedProbeMeansAlive()
    {
        var health = new HookHealth(0);
        health.KeySeen(100_000);
        long now = 100_000 + Silence;

        Assert.Equal(HookHealthAction.Probe, health.Check(now, lastInput: now - 10));
        health.KeySeen(now + 5);

        Assert.Equal(HookHealthAction.None, health.Check(now + Timeout, lastInput: now + 5));
        Assert.Equal(HookHealthAction.None, health.Check(now + Timeout * 5, lastInput: now + 5));
    }

    [Fact]
    public void AProbeTheHookNeverSees_ReinstallsIt()
    {
        var health = new HookHealth(0);
        health.KeySeen(100_000);
        long now = 100_000 + Silence;

        Assert.Equal(HookHealthAction.Probe, health.Check(now, lastInput: now - 10));
        Assert.Equal(HookHealthAction.None, health.Check(now + Timeout - 1, lastInput: now));
        Assert.Equal(HookHealthAction.Reinstall, health.Check(now + Timeout, lastInput: now));

        // The reinstalled hook counts as new: no probe straight away
        Assert.Equal(HookHealthAction.None, health.Check(now + Timeout + 1, lastInput: now + Timeout + 1));
    }

    [Fact]
    public void Probes_AreRateLimited_ForMouseUsers()
    {
        var health = new HookHealth(0);
        long now = 100_000;

        Assert.Equal(HookHealthAction.Probe, health.Check(now, lastInput: now - 1));
        health.KeySeen(now + 1);
        Assert.Equal(HookHealthAction.None, health.Check(now + 2, lastInput: now + 1));

        // The mouse keeps moving; the hook (rightly) sees no keys
        Assert.Equal(HookHealthAction.None, health.Check(now + Interval - 1, lastInput: now + Interval - 2));
        Assert.Equal(HookHealthAction.Probe, health.Check(now + Interval + 1, lastInput: now + Interval));
    }
}
