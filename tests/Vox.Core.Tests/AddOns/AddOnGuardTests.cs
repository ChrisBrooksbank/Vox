using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.AddOns;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.AddOns;

public class AddOnGuardTests
{
    private readonly ManualTimeProvider _time = new();
    private readonly List<AddOnGuard> _turnedOff = [];

    private AddOnGuard Guard() => new("x", NullLogger.Instance, g => _turnedOff.Add(g), _time);

    [Fact]
    public void AQuickCall_IsNotCounted()
    {
        var guard = Guard();

        Assert.True(guard.Run(() => _time.Advance(TimeSpan.FromMilliseconds(40)), "testing"));

        Assert.Equal(0, guard.RecentFailures);
    }

    [Fact]
    public void ACallOverItsBudget_CountsAsAFailure_ButItsResultStands()
    {
        var guard = Guard();

        var result = guard.Call(() =>
        {
            _time.Advance(TimeSpan.FromMilliseconds(60));
            return "done";
        }, "fallback", "testing");

        Assert.Equal("done", result);
        Assert.Equal(1, guard.RecentFailures);
        Assert.False(guard.IsStopped);
    }

    [Fact]
    public void FiltersHaveASmallerBudget()
    {
        var guard = Guard();

        guard.Call(() =>
        {
            _time.Advance(TimeSpan.FromMilliseconds(20));
            return "text";
        }, "text", "filtering speech", AddOnGuard.FilterBudget);

        Assert.Equal(1, guard.RecentFailures);
    }

    [Fact]
    public void RepeatedlySlowCalls_TurnTheAddOnOff()
    {
        var guard = Guard();

        for (int i = 0; i < AddOnGuard.MaxFailures; i++)
            guard.Run(() => _time.Advance(TimeSpan.FromMilliseconds(100)), "testing");

        Assert.True(guard.IsStopped);
        Assert.False(guard.HasHung);
        Assert.Same(guard, Assert.Single(_turnedOff));
    }

    [Fact]
    public void ACallStillRunningPastTheHangLimit_StopsTheAddOn_AndItsResultIsDropped()
    {
        var guard = Guard();

        var result = guard.Call(() =>
        {
            _time.Advance(AddOnGuard.HangLimit);
            Assert.True(guard.CheckForHang()); // the watchdog, while the call is still running
            return "late";
        }, "fallback", "handling an event");

        Assert.Equal("fallback", result);
        Assert.True(guard.IsStopped);
        Assert.True(guard.HasHung);
        Assert.Same(guard, Assert.Single(_turnedOff));
        // Its pending calls are skipped
        Assert.False(guard.Run(() => Assert.Fail("ran"), "testing"));
    }

    [Fact]
    public void CheckForHang_IgnoresCallsWithinTheLimit_AndIdleAddOns()
    {
        var guard = Guard();

        Assert.False(guard.CheckForHang());
        guard.Run(() =>
        {
            _time.Advance(AddOnGuard.HangLimit - TimeSpan.FromMilliseconds(1));
            Assert.False(guard.CheckForHang());
        }, "testing");
        _time.Advance(TimeSpan.FromSeconds(10));

        Assert.False(guard.CheckForHang());
        Assert.False(guard.IsStopped);
    }

    [Fact]
    public void AHangIsReportedOnce()
    {
        var guard = Guard();

        guard.Run(() =>
        {
            _time.Advance(TimeSpan.FromSeconds(5));
            guard.CheckForHang();
            guard.CheckForHang();
        }, "testing");

        Assert.Single(_turnedOff);
    }

    [Fact]
    public void TheWatchdog_FindsAHungCallFromItsTimer()
    {
        using var watchdog = new AddOnWatchdog(_time);
        var guard = Guard();
        watchdog.Watch(guard);

        guard.Run(() => _time.Advance(AddOnGuard.HangLimit + AddOnWatchdog.Interval), "testing");

        Assert.True(guard.HasHung);
        Assert.Single(_turnedOff);
    }

    [Fact]
    public void TheWatchdogTimer_RunsOnlyWhileThereAreGuards()
    {
        using var watchdog = new AddOnWatchdog(_time);
        var guard = Guard();

        watchdog.Watch(guard);
        Assert.Equal(1, _time.ActiveTimers);

        watchdog.Forget(guard);
        Assert.Equal(0, _time.ActiveTimers);
    }
}
