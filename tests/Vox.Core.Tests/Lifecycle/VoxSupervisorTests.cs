using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Lifecycle;
using Xunit;

namespace Vox.Core.Tests.Lifecycle;

public class VoxSupervisorTests
{
    private sealed class FakeProcess(int exitCode) : IRunningProcess
    {
        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => Task.FromResult(exitCode);
        public void Dispose() { }
    }

    private sealed class HangingProcess : IRunningProcess
    {
        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => 0, cancellationToken);
        public void Dispose() { }
    }

    private sealed class ScriptedLauncher(params Func<IRunningProcess>[] runs) : IProcessLauncher
    {
        public int Starts { get; private set; }
        public IRunningProcess Start() => runs[Math.Min(Starts++, runs.Length - 1)]();
    }

    private DateTimeOffset _now = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);

    private VoxSupervisor Create(ScriptedLauncher launcher, Action? onGiveUp = null) =>
        new(launcher, new RestartPolicy(3, TimeSpan.FromMinutes(1), () => _now), onGiveUp ?? (() => { }),
            NullLogger.Instance);

    [Fact]
    public async Task NormalExit_IsNotRestarted()
    {
        var launcher = new ScriptedLauncher(() => new FakeProcess(VoxExitCodes.Normal));

        var code = await Create(launcher).RunAsync();

        Assert.Equal(0, code);
        Assert.Equal(1, launcher.Starts);
    }

    [Fact]
    public async Task AlreadyRunning_IsNotRestarted()
    {
        var launcher = new ScriptedLauncher(() => new FakeProcess(VoxExitCodes.AlreadyRunning));

        await Create(launcher).RunAsync();

        Assert.Equal(1, launcher.Starts);
    }

    [Fact]
    public async Task Crash_IsRestarted_UntilNormalExit()
    {
        var launcher = new ScriptedLauncher(
            () => new FakeProcess(VoxExitCodes.Fatal),
            () => new FakeProcess(unchecked((int)0xE0434352)), // unhandled .NET exception
            () => new FakeProcess(VoxExitCodes.Normal));

        var code = await Create(launcher).RunAsync();

        Assert.Equal(0, code);
        Assert.Equal(3, launcher.Starts);
    }

    [Fact]
    public async Task CrashLoop_GivesUpAfterThreeRestartsAndTellsTheUser()
    {
        var gaveUp = 0;
        var launcher = new ScriptedLauncher(() => new FakeProcess(VoxExitCodes.Fatal));

        var code = await Create(launcher, () => gaveUp++).RunAsync();

        Assert.Equal(VoxExitCodes.Fatal, code);
        Assert.Equal(4, launcher.Starts); // the first start plus three restarts
        Assert.Equal(1, gaveUp);
    }

    [Fact]
    public void RestartPolicy_AllowsMoreRestartsOnceTheWindowHasPassed()
    {
        var policy = new RestartPolicy(3, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(policy.TryRestart());
        Assert.True(policy.TryRestart());
        Assert.True(policy.TryRestart());
        Assert.False(policy.TryRestart());

        _now += TimeSpan.FromMinutes(1);
        Assert.True(policy.TryRestart());
    }

    [Fact]
    public async Task Cancellation_StopsSupervisingWithoutRestart()
    {
        var launcher = new ScriptedLauncher(() => new HangingProcess());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var code = await Create(launcher).RunAsync(cts.Token);

        Assert.Equal(VoxExitCodes.Normal, code);
        Assert.Equal(1, launcher.Starts);
    }
}
