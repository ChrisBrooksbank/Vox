using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Accessibility;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class UIARecoveryTests : IDisposable
{
    private readonly UIAThread _uiaThread = new(NullLogger<UIAThread>.Instance);

    public void Dispose() => _uiaThread.Dispose();

    [Fact]
    public async Task RecoverAsync_RunsStepsInOrder()
    {
        var order = new List<string>();
        using var recovery = new UIARecovery(_uiaThread, NullLogger<UIARecovery>.Instance,
            ("a", () => { order.Add("a"); return Task.CompletedTask; }),
            ("b", () => { order.Add("b"); return Task.CompletedTask; }),
            ("c", () => { order.Add("c"); return Task.CompletedTask; }));

        await recovery.RecoverAsync();

        Assert.Equal(["a", "b", "c"], order);
    }

    [Fact]
    public async Task RecoverAsync_FailingStep_LaterStepsStillRunAndReportsFailure()
    {
        var ran = new List<string>();
        using var recovery = new UIARecovery(_uiaThread, NullLogger<UIARecovery>.Instance,
            ("a", () => throw new InvalidOperationException("boom")),
            ("b", () => { ran.Add("b"); return Task.CompletedTask; }));
        bool? result = null;
        recovery.Recovered += (_, ok) => result = ok;

        await recovery.RecoverAsync();

        Assert.Equal(["b"], ran);
        Assert.False(result);
    }

    [Fact]
    public async Task ThreadReplaced_TriggersRecovery()
    {
        var recovered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var recovery = new UIARecovery(_uiaThread, NullLogger<UIARecovery>.Instance,
            ("step", () => Task.CompletedTask));
        recovery.Recovered += (_, ok) => recovered.TrySetResult(ok);

        _uiaThread.ReplaceStuckThread();

        Assert.True(await recovered.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task ReplacementDuringRecovery_RunsRecoveryAgainAfterwards()
    {
        var runs = 0;
        var firstRunGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var recovery = new UIARecovery(_uiaThread, NullLogger<UIARecovery>.Instance,
            ("step", async () =>
            {
                if (Interlocked.Increment(ref runs) == 1)
                    await firstRunGate.Task;
            }));

        var first = recovery.RecoverAsync();
        var second = recovery.RecoverAsync(); // while the first is still running
        firstRunGate.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, runs);
    }
}
