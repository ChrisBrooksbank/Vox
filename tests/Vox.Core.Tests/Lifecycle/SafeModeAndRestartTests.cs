using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Lifecycle;
using Xunit;

namespace Vox.Core.Tests.Lifecycle;

public class SafeModeAndRestartTests
{
    [Fact]
    public void TheSafeFlag_TurnsSafeModeOn()
    {
        Assert.True(RunPolicy.FromArgs(["--safe"]).SafeMode);
        Assert.True(RunPolicy.FromArgs(["/SAFE"]).SafeMode);
        Assert.False(RunPolicy.FromArgs([]).SafeMode);
        Assert.False(RunPolicy.FromArgs(["--safe"]).IsSecure);
    }

    [Fact]
    public void TheSecureScreens_HaveNoAddOns_SafeOrNot()
    {
        var policy = RunPolicy.FromArgs(["--secure", "--safe"]);

        Assert.True(policy.IsSecure);
        Assert.False(policy.AllowAddOns);
    }

    [Fact]
    public void ARestart_KeepsTheOtherArguments_AndSetsTheMode()
    {
        Assert.Equal(["--verbose", "--safe", "--restarted"],
            RestartRequest.ArgumentsFor(["--verbose", "--restarted"], safeMode: true, supervised: false));
        Assert.Equal(["--verbose", "--supervised"],
            RestartRequest.ArgumentsFor(["--verbose", "--safe", "--supervised"], safeMode: false, supervised: true));
    }

    [Fact]
    public void ARequest_IsRemembered_WithItsExitCode()
    {
        var request = new RestartRequest();
        Assert.Null(request.SafeMode);

        request.Request(safeMode: true);

        Assert.True(request.SafeMode);
        Assert.Equal(VoxExitCodes.RestartSafe, RestartRequest.ExitCode(true));
        Assert.Equal(VoxExitCodes.RestartNormal, RestartRequest.ExitCode(false));
    }

    private sealed class FakeProcess(int exitCode) : IRunningProcess
    {
        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => Task.FromResult(exitCode);
        public void Dispose() { }
    }

    private sealed class RecordingLauncher(params int[] exitCodes) : IProcessLauncher
    {
        public List<bool> Starts { get; } = [];
        public IRunningProcess Start() => Start(false);
        public IRunningProcess Start(bool safeMode)
        {
            Starts.Add(safeMode);
            return new FakeProcess(exitCodes[Math.Min(Starts.Count - 1, exitCodes.Length - 1)]);
        }
    }

    [Fact]
    public async Task TheWatchdog_RestartsAsAsked_WithoutCountingItAsACrash()
    {
        var launcher = new RecordingLauncher(VoxExitCodes.RestartSafe, VoxExitCodes.RestartSafe, VoxExitCodes.RestartSafe,
            VoxExitCodes.RestartSafe, VoxExitCodes.RestartNormal, VoxExitCodes.Normal);
        var supervisor = new VoxSupervisor(launcher, new RestartPolicy(1), () => Assert.Fail("gave up"), NullLogger.Instance);

        var code = await supervisor.RunAsync();

        Assert.Equal(VoxExitCodes.Normal, code);
        Assert.Equal([false, true, true, true, true, false], launcher.Starts);
    }

    [Fact]
    public async Task TheWatchdog_StartsInSafeMode_WhenItWasGivenTheFlag()
    {
        var launcher = new RecordingLauncher(VoxExitCodes.Fatal, VoxExitCodes.Normal);
        var supervisor = new VoxSupervisor(launcher, new RestartPolicy(), () => { }, NullLogger.Instance, startInSafeMode: true);

        await supervisor.RunAsync();

        // A crash restarts it in the same mode
        Assert.Equal([true, true], launcher.Starts);
    }
}
