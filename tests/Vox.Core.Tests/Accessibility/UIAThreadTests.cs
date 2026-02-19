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
}
