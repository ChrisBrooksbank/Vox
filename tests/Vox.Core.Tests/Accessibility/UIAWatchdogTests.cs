using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Accessibility;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class UIAWatchdogTests : IDisposable
{
    private readonly UIAThread _uiaThread = new(NullLogger<UIAThread>.Instance);
    private readonly ManualResetEventSlim _release = new();

    public void Dispose()
    {
        _release.Set();
        _uiaThread.Dispose();
        _release.Dispose();
    }

    private UIAWatchdog CreateWatchdog(TimeSpan threshold) =>
        new(_uiaThread, NullLogger<UIAWatchdog>.Instance, threshold, TimeSpan.FromHours(1));

    private async Task StartStuckCallAsync(TimeSpan timeout)
    {
        using var started = new ManualResetEventSlim();
        _ = _uiaThread.RunAsync(() => { started.Set(); _release.Wait(); }, timeout)
            .ContinueWith(_ => { }); // its timeout is expected
        Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
        await Task.Yield();
    }

    [Theory]
    [InlineData(null, 2000, false)]      // idle
    [InlineData(1000, 2000, false)]      // within its timeout and the threshold
    [InlineData(3000, 2000, false)]      // past its timeout but not the 5 s threshold
    [InlineData(5000, 2000, true)]       // past both
    [InlineData(10000, 25000, false)]    // a long document capture still within its own timeout
    [InlineData(25000, 25000, true)]
    [InlineData(60000, -1, false)]       // no timeout: never considered stuck
    public void IsStuck_NeedsBothItsTimeoutAndTheThreshold(int? durationMs, int timeoutMs, bool expected)
    {
        TimeSpan? duration = durationMs is null ? null : TimeSpan.FromMilliseconds(durationMs.Value);
        var timeout = timeoutMs < 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(timeoutMs);

        Assert.Equal(expected, UIAWatchdog.IsStuck(duration, timeout, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Check_IdleThread_DoesNothing()
    {
        using var watchdog = CreateWatchdog(TimeSpan.FromMilliseconds(50));

        Assert.False(watchdog.Check());
        Assert.Equal(0, _uiaThread.Generation);
    }

    [Fact]
    public async Task Check_StuckCall_ReplacesThreadAndLaterCallsRunOnTheNewOne()
    {
        using var watchdog = CreateWatchdog(TimeSpan.FromMilliseconds(100));
        var replaced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiaThread.ThreadReplaced += (_, _) => replaced.TrySetResult();
        var oldThreadId = await _uiaThread.RunAsync(() => Environment.CurrentManagedThreadId);

        await StartStuckCallAsync(TimeSpan.FromMilliseconds(50));
        await Task.Delay(150);

        Assert.True(watchdog.Check());
        await replaced.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, _uiaThread.Generation);

        // The old thread is still stuck, yet new calls complete, on a different thread
        var newThreadId = await _uiaThread.RunAsync(() => Environment.CurrentManagedThreadId);
        Assert.NotEqual(oldThreadId, newThreadId);
        Assert.Equal(newThreadId, _uiaThread.ManagedThreadId);
    }

    [Fact]
    public async Task ReplaceStuckThread_MovesQueuedCallsToTheNewThread()
    {
        await StartStuckCallAsync(Timeout.InfiniteTimeSpan);
        var queued = _uiaThread.RunAsync(() => "ran", TimeSpan.FromSeconds(5));

        _uiaThread.ReplaceStuckThread();

        Assert.Equal("ran", await queued.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Check_CallWithinItsLongTimeout_IsLeftAlone()
    {
        using var watchdog = CreateWatchdog(TimeSpan.FromMilliseconds(50));
        await StartStuckCallAsync(TimeSpan.FromSeconds(30)); // like a document capture
        await Task.Delay(120);

        Assert.False(watchdog.Check());
        Assert.Equal(0, _uiaThread.Generation);
    }

    [Fact]
    public async Task StuckThreadReturningLater_DoesNotRunWorkMovedToTheNewThread()
    {
        await StartStuckCallAsync(Timeout.InfiniteTimeSpan);
        var count = 0;
        var queued = _uiaThread.RunAsync(() => Interlocked.Increment(ref count), TimeSpan.FromSeconds(5));

        _uiaThread.ReplaceStuckThread();
        await queued.WaitAsync(TimeSpan.FromSeconds(2));
        _release.Set();
        await Task.Delay(50);

        Assert.Equal(1, count);
    }
}
