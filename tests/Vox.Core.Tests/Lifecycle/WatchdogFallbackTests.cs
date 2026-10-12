using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Lifecycle;
using Vox.Core.Tests.TestSupport;
using Vox.Core.Tests.Trial;
using Vox.Core.Trial;
using Xunit;

namespace Vox.Core.Tests.Lifecycle;

public sealed class WatchdogFallbackTests : IDisposable
{
    private const string NvdaPath = @"C:\Program Files (x86)\NVDA\nvda.exe";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VoxFallback_" + Guid.NewGuid().ToString("N"));
    private readonly FakeScreenReaderControl _control = new();
    private readonly ManualTimeProvider _time = new();

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string TrialPath => Path.Combine(_directory, TrialRecord.FileName);
    private string NoticePath => Path.Combine(_directory, FallbackNotice.FileName);

    private WatchdogFallback Fallback() => new(_control, TrialPath, NoticePath, NullLogger.Instance, _time);

    [Fact]
    public void Choose_IsTheTrialsScreenReader_ElseNarrator()
    {
        Assert.Equal(OtherScreenReader.Narrator, WatchdogFallback.Choose(null));
        Assert.Equal(OtherScreenReader.Nvda, WatchdogFallback.Choose(new TrialRecord(OtherScreenReader.Nvda, NvdaPath, default)));
        // A trial with nothing running before goes back to Narrator
        Assert.Equal(OtherScreenReader.Narrator, WatchdogFallback.Choose(new TrialRecord(null, null, default)));
    }

    [Fact]
    public void WithoutATrial_NarratorIsStarted_AndANoticeLeft()
    {
        var notice = Fallback().Run(crashes: 4);

        Assert.Equal((OtherScreenReader.Narrator, (string?)null), Assert.Single(_control.Started));
        Assert.Equal(new FallbackNotice(OtherScreenReader.Narrator, true, 4, _time.GetUtcNow()), notice);
        Assert.Equal(notice, FallbackNotice.Read(NoticePath));
    }

    [Fact]
    public void DuringATrial_ItsScreenReaderIsStartedFromWhereItRan_AndTheTrialEnds()
    {
        new TrialRecord(OtherScreenReader.Nvda, NvdaPath, default).Write(TrialPath);

        var notice = Fallback().Run(crashes: 4);

        Assert.Equal((OtherScreenReader.Nvda, NvdaPath), Assert.Single(_control.Started));
        Assert.Equal(OtherScreenReader.Nvda, notice.ScreenReader);
        Assert.False(File.Exists(TrialPath));
    }

    [Fact]
    public void AScreenReaderAlreadyRunning_IsLeftAlone()
    {
        _control.Running = new RunningScreenReader(OtherScreenReader.Nvda, NvdaPath);

        var notice = Fallback().Run(crashes: 4);

        Assert.Empty(_control.Started);
        Assert.Equal(OtherScreenReader.Nvda, notice.ScreenReader);
        Assert.True(notice.Started);
    }

    [Fact]
    public void WhenNothingCanBeStarted_TheNoticeSaysSo_AndTheTrialIsKept()
    {
        new TrialRecord(OtherScreenReader.Nvda, NvdaPath, default).Write(TrialPath);
        _control.StartWorks = false;

        var notice = Fallback().Run(crashes: 4);

        Assert.False(notice.Started);
        Assert.True(File.Exists(TrialPath));
        Assert.Equal("Last time Vox stopped working 4 times in a row, and NVDA could not be started in its place. "
            + "If it happens again, try Restart with add-ons disabled in the Vox menu, and send the log to support.", notice.Message());
    }

    [Fact]
    public void TheNextStart_SaysWhyOnce()
    {
        Fallback().Run(crashes: 4);

        var message = FallbackNotice.TakeMessage(NoticePath);

        Assert.StartsWith("Last time Vox stopped working 4 times in a row, so Narrator was started in its place.", message);
        Assert.False(File.Exists(NoticePath));
        Assert.Null(FallbackNotice.TakeMessage(NoticePath));
    }

    [Fact]
    public async Task TheSupervisor_FallsBack_OnlyWhenItGivesUp()
    {
        int fallbacks = 0;
        var launcher = new CrashingLauncher();
        var supervisor = new VoxSupervisor(launcher, new RestartPolicy(maxRestarts: 3), () => fallbacks++,
            NullLogger.Instance);

        await supervisor.RunAsync();

        Assert.Equal(4, launcher.Starts);
        Assert.Equal(1, fallbacks);
    }

    private sealed class CrashingLauncher : IProcessLauncher
    {
        public int Starts { get; private set; }

        public IRunningProcess Start()
        {
            Starts++;
            return new Crashed();
        }

        private sealed class Crashed : IRunningProcess
        {
            public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => Task.FromResult(VoxExitCodes.Fatal);
            public void Dispose() { }
        }
    }
}
