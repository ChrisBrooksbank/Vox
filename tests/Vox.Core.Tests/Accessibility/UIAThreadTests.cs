using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Accessibility;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class UIAThreadTests : IDisposable
{
    private readonly UIAThread _uiaThread;

    public UIAThreadTests()
    {
        _uiaThread = new UIAThread(NullLogger<UIAThread>.Instance);
    }

    public void Dispose() => _uiaThread.Dispose();

    [Fact]
    public async Task RunAsync_ReturnsResultFromSTA()
    {
        var result = await _uiaThread.RunAsync(() => 42);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task RunAsync_ExecutesOnSTAThread()
    {
        var apartmentState = await _uiaThread.RunAsync(
            () => Thread.CurrentThread.GetApartmentState());

        Assert.Equal(ApartmentState.STA, apartmentState);
    }

    [Fact]
    public async Task RunAsync_ExecutesOnDedicatedThread()
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        var workerThreadId = await _uiaThread.RunAsync(
            () => Environment.CurrentManagedThreadId);

        Assert.NotEqual(callerThreadId, workerThreadId);
    }

    [Fact]
    public async Task RunAsync_PropagatesExceptions()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _uiaThread.RunAsync<int>(() => throw new InvalidOperationException("test error")));
    }

    [Fact]
    public async Task RunAsync_Action_CompletesSuccessfully()
    {
        var ran = false;
        await _uiaThread.RunAsync(() => { ran = true; });
        Assert.True(ran);
    }

    [Fact]
    public async Task RunAsync_MultipleCallsSerializedOnSameThread()
    {
        var threadIds = await Task.WhenAll(
            _uiaThread.RunAsync(() => Environment.CurrentManagedThreadId),
            _uiaThread.RunAsync(() => Environment.CurrentManagedThreadId),
            _uiaThread.RunAsync(() => Environment.CurrentManagedThreadId));

        Assert.All(threadIds, id => Assert.Equal(threadIds[0], id));
    }

    [Fact]
    public void RunAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        _uiaThread.Dispose();
        // RunAsync throws synchronously (before returning a Task) when disposed
        var ex = Record.Exception(() => { _ = _uiaThread.RunAsync(() => 1); });
        Assert.IsType<ObjectDisposedException>(ex);
    }

    // -------------------------------------------------------------------------
    // Timeouts: a stuck call never makes its callers wait forever
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_CallOutlastsTimeout_ThrowsUIATimeoutException()
    {
        using var release = new ManualResetEventSlim();
        try
        {
            var ex = await Assert.ThrowsAsync<UIATimeoutException>(() =>
                _uiaThread.RunAsync(() => { release.Wait(); return 1; }, TimeSpan.FromMilliseconds(100)));
            Assert.Equal(TimeSpan.FromMilliseconds(100), ex.Timeout);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task RunAsync_QueuedCallTimesOutBehindStuckCall_IsNeverStarted()
    {
        using var release = new ManualResetEventSlim();
        var stuck = _uiaThread.RunAsync(() => { release.Wait(); return 1; }, Timeout.InfiniteTimeSpan);
        var queuedRan = false;

        await Assert.ThrowsAsync<UIATimeoutException>(() =>
            _uiaThread.RunAsync(() => { queuedRan = true; return 2; }, TimeSpan.FromMilliseconds(50)));

        release.Set();
        Assert.Equal(1, await stuck);
        Assert.Equal(3, await _uiaThread.RunAsync(() => 3)); // the thread has moved past the skipped call
        Assert.False(queuedRan);
    }

    [Fact]
    public async Task RunAsync_AfterAbandonedCallReturns_ThreadServesLaterCalls()
    {
        using var release = new ManualResetEventSlim();
        await Assert.ThrowsAsync<UIATimeoutException>(() =>
            _uiaThread.RunAsync(() => { release.Wait(); return 1; }, TimeSpan.FromMilliseconds(50)));

        release.Set();

        Assert.Equal(7, await _uiaThread.RunAsync(() => 7));
    }

    [Fact]
    public async Task RunAsync_ExceptionAfterTimeout_IsNotRethrownOrLost()
    {
        using var release = new ManualResetEventSlim();
        var task = _uiaThread.RunAsync<int>(() => { release.Wait(); throw new InvalidOperationException("late"); },
            TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<UIATimeoutException>(() => task);
        release.Set();

        Assert.Equal(1, await _uiaThread.RunAsync(() => 1));
    }

    [Fact]
    public async Task RunAsync_DefaultTimeout_IsTwoSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), UIAThread.DefaultTimeout);
        Assert.Equal(5, await _uiaThread.RunAsync(() => 5));
    }

    [Fact]
    public async Task CurrentWorkDuration_SetWhileRunning_NullWhenIdle()
    {
        Assert.Null(_uiaThread.CurrentWorkDuration);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var task = _uiaThread.RunAsync(() => { started.Set(); release.Wait(); }, Timeout.InfiniteTimeSpan);

        Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
        await Task.Delay(60);
        Assert.True(_uiaThread.CurrentWorkDuration >= TimeSpan.FromMilliseconds(50));

        release.Set();
        await task;
        await Task.Delay(20);
        Assert.Null(_uiaThread.CurrentWorkDuration);
    }
}
