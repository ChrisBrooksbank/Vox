using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Vox.Core.Trial;
using Xunit;

namespace Vox.Core.Tests.Trial;

/// <summary>NVDA and Narrator as a test needs them: what runs, what was stopped and started.</summary>
public sealed class FakeScreenReaderControl : IScreenReaderControl
{
    public RunningScreenReader? Running { get; set; }
    public bool StopWorks { get; set; } = true;
    public bool StartWorks { get; set; } = true;
    public List<RunningScreenReader> Stopped { get; } = new();
    public List<(OtherScreenReader Kind, string? Path)> Started { get; } = new();

    public RunningScreenReader? FindRunning() => Running;

    public bool Stop(RunningScreenReader reader)
    {
        Stopped.Add(reader);
        if (StopWorks)
            Running = null;
        return StopWorks;
    }

    public bool Start(OtherScreenReader kind, string? executablePath = null)
    {
        Started.Add((kind, executablePath));
        if (StartWorks)
            Running = new RunningScreenReader(kind, executablePath);
        return StartWorks;
    }
}

public sealed class TrialModeTests : IDisposable
{
    private const string NvdaPath = @"C:\Program Files (x86)\NVDA\nvda.exe";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VoxTrial_" + Guid.NewGuid().ToString("N"));
    private readonly FakeScreenReaderControl _control = new();
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _speech;
    private int _quits;

    public TrialModeTests()
    {
        _speech = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
    }

    public void Dispose()
    {
        _speech.Dispose();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string RecordPath => Path.Combine(_directory, TrialRecord.FileName);

    private TrialMode Trial(RunPolicy? policy = null) =>
        new(_control, RecordPath, policy ?? RunPolicy.Normal, () => _quits++, NullLogger<TrialMode>.Instance, _speech)
        {
            ReturnKeyName = () => "Insert+Ctrl+F4",
        };

    [Fact]
    public void Begin_StopsARunningNvda_AndRecordsIt()
    {
        _control.Running = new RunningScreenReader(OtherScreenReader.Nvda, NvdaPath);
        var trial = Trial();
        bool? changed = null;
        trial.ActiveChanged += (_, active) => changed = active;

        var said = trial.Begin();

        Assert.Equal("Trial mode. NVDA has been stopped while you try Vox. Insert+Ctrl+F4 quits Vox and starts NVDA again.", said);
        Assert.Equal(OtherScreenReader.Nvda, Assert.Single(_control.Stopped).Kind);
        Assert.Null(_control.Running);
        var record = TrialRecord.Read(RecordPath)!;
        Assert.Equal(OtherScreenReader.Nvda, record.ScreenReader);
        Assert.Equal(NvdaPath, record.ExecutablePath);
        Assert.True(trial.IsActive);
        Assert.True(changed);
    }

    [Fact]
    public void Begin_WithNothingRunning_GoesBackToNarrator()
    {
        var trial = Trial();

        var said = trial.Begin();

        Assert.Equal("Trial mode. Insert+Ctrl+F4 quits Vox and starts Narrator.", said);
        Assert.Empty(_control.Stopped);
        Assert.Equal(OtherScreenReader.Narrator, trial.Current!.ReturnTo);
        Assert.Null(trial.Current.ScreenReader);
    }

    [Fact]
    public void Begin_Twice_KeepsTheFirstTrial()
    {
        _control.Running = new RunningScreenReader(OtherScreenReader.Narrator);
        var trial = Trial();
        trial.BeginTrial();

        // Vox restarted with --trial: Narrator is no longer running, but the trial is for it
        var again = trial.BeginTrial();

        Assert.Equal(OtherScreenReader.Narrator, again!.ScreenReader);
        Assert.Single(_control.Stopped);
        // ... and it is no longer offered
        Assert.Null(trial.FindRunning());
    }

    [Fact]
    public async Task Return_StartsTheScreenReaderAgain_EndsTheTrial_AndQuits()
    {
        _control.Running = new RunningScreenReader(OtherScreenReader.Nvda, NvdaPath);
        var trial = Trial();
        trial.BeginTrial();

        Assert.True(trial.TryHandle(NavigationCommand.ReturnToScreenReader));
        await WaitUntil(() => _quits > 0);

        Assert.Equal((OtherScreenReader.Nvda, NvdaPath), Assert.Single(_control.Started));
        Assert.False(trial.IsActive);
        Assert.False(File.Exists(RecordPath));
        Assert.Contains(_engine.Spoken, s => s.Text == "Going back to NVDA");
    }

    [Fact]
    public async Task Return_WhenItCannotBeStarted_KeepsVoxRunning()
    {
        _control.Running = new RunningScreenReader(OtherScreenReader.Nvda, NvdaPath);
        _control.StartWorks = false;
        var trial = Trial();
        trial.BeginTrial();

        Assert.False(await trial.ReturnAsync());

        Assert.Equal(0, _quits);
        Assert.True(trial.IsActive);
        await _engine.WaitForAsync(s => s.Text == "Could not start NVDA; Vox keeps running");
    }

    [Fact]
    public async Task Return_OutsideATrial_DoesNothing()
    {
        var trial = Trial();

        Assert.False(await trial.ReturnAsync());

        Assert.Empty(_control.Started);
        Assert.Equal(0, _quits);
        await _engine.WaitForAsync(s => s.Text == "Vox is not in trial mode");
    }

    [Fact]
    public async Task SecureScreens_HaveNoTrialMode()
    {
        _control.Running = new RunningScreenReader(OtherScreenReader.Narrator);
        var trial = Trial(RunPolicy.Secure);

        Assert.False(trial.IsAvailable);
        Assert.Null(trial.FindRunning());
        Assert.Null(trial.BeginTrial());
        Assert.False(await trial.ReturnAsync());
        Assert.Empty(_control.Stopped);
        Assert.Empty(_control.Started);
        Assert.False(File.Exists(RecordPath));
    }

    [Fact]
    public void OtherCommands_AreNotTrialModes()
    {
        Assert.False(Trial().TryHandle(NavigationCommand.Quit));
    }

    [Fact]
    public void TheRecord_SurvivesAWriteAndRead_AndABadFileIsNone()
    {
        var record = new TrialRecord(OtherScreenReader.Nvda, NvdaPath, new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));
        Assert.True(record.Write(RecordPath));
        Assert.Equal(record, TrialRecord.Read(RecordPath));

        File.WriteAllText(RecordPath, "{ not json");
        Assert.Null(TrialRecord.Read(RecordPath));
        Assert.Null(TrialRecord.Read(null));
    }

    [Theory]
    [InlineData(new[] { "--trial" }, true)]
    [InlineData(new[] { "/trial", "--safe" }, true)]
    [InlineData(new[] { "--safe" }, false)]
    [InlineData(new[] { "--secure", "--trial" }, false)]
    public void TheTrialFlag_IsReadFromTheCommandLine(string[] args, bool trial) =>
        Assert.Equal(trial, RunPolicy.FromArgs(args).StartTrial);

    [Fact]
    public void TheVoxMenu_OffersTheReturn_OnlyDuringATrial()
    {
        Assert.DoesNotContain(VoxMenu.Items, i => i.Command == NavigationCommand.ReturnToScreenReader);
        VoxMenu.TrialActive = true;
        try
        {
            var items = VoxMenu.Items;
            Assert.Contains(items, i => i.Command == NavigationCommand.ReturnToScreenReader);
            var keys = items.Select(i => char.ToLowerInvariant(i.Text[i.Text.IndexOf('&') + 1])).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }
        finally
        {
            VoxMenu.TrialActive = false;
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException();
            await Task.Delay(10);
        }
    }
}
