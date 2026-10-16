using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Magnifier;
using Vox.Core.Ocr;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Magnifier;

/// <summary>A magnifier that records what it was asked to show.</summary>
public sealed class FakeMagnifier : IMagnifier
{
    public MagnifierStartResult StartResult { get; set; } = MagnifierStartResult.Started;
    public bool IsRunning { get; private set; }
    public List<MagnifierFrame> Frames { get; } = [];
    public int Starts { get; private set; }
    public int Stops { get; private set; }
    public MagnifierFrame? Last => Frames.Count > 0 ? Frames[^1] : null;

    public MagnifierStartResult Start()
    {
        Starts++;
        IsRunning = StartResult == MagnifierStartResult.Started;
        return StartResult;
    }

    public void Stop()
    {
        if (IsRunning)
            Stops++;
        IsRunning = false;
    }

    public void Show(MagnifierFrame frame)
    {
        if (IsRunning)
            Frames.Add(frame);
    }

    public void Dispose() => Stop();
}

public sealed class FakeDisplays(params ScreenRect[] displays) : IDisplayLayout
{
    public IReadOnlyList<ScreenRect> Displays { get; set; } = displays.Length > 0 ? displays : [new ScreenRect(0, 0, 1920, 1080)];
}

public sealed class RecordingCues : IAudioCuePlayer
{
    public List<string> Played { get; } = [];
    public bool IsEnabled { get; set; } = true;
    public void Play(string cueName) => Played.Add(cueName);
}

public sealed class MagnifierControllerTests : IDisposable
{
    private readonly FakeMagnifier _magnifier = new();
    private readonly FakeDisplays _displays = new();
    private readonly TestOptionsMonitor<VoxSettings> _settings;
    private readonly ManualTimeProvider _time = new();
    private readonly RecordingCues _cues = new();
    private readonly List<string> _said = [];
    private MagnifierController _controller;

    public MagnifierControllerTests()
    {
        _settings = new TestOptionsMonitor<VoxSettings>(new VoxSettings { MagnifierSmoothTracking = false });
        _controller = Create();
    }

    private MagnifierController Create(RunPolicy? policy = null) =>
        new(_magnifier, _displays, _settings, _settings.Set, _said.Add, NullLogger<MagnifierController>.Instance,
            policy, _cues, _time);

    public void Dispose()
    {
        _controller.Dispose();
    }

    private void Set(Func<VoxSettings, VoxSettings> change) => _settings.Set(change(_settings.CurrentValue));

    private void Run(NavigationCommand command) => Assert.True(_controller.TryHandle(command));

    // -------------------------------------------------------------------------
    // On and off
    // -------------------------------------------------------------------------

    [Fact]
    public void Toggle_TurnsItOnAtTheSettingsZoom_SaysSo_AndRemembersIt()
    {
        _controller.Start();

        Run(NavigationCommand.ToggleMagnifier);

        Assert.True(_controller.IsOn);
        Assert.True(_settings.CurrentValue.MagnifierEnabled);
        Assert.Equal(2, _magnifier.Last!.Zoom);
        Assert.Equal(MagnifierView.FullScreen, _magnifier.Last.View);
        Assert.Null(_magnifier.Last.Window);
        Assert.Equal("Magnifier on, 2 times", Assert.Single(_said));
    }

    [Fact]
    public void Toggle_Again_TurnsItOff()
    {
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier);

        Run(NavigationCommand.ToggleMagnifier);

        Assert.False(_controller.IsOn);
        Assert.False(_magnifier.IsRunning);
        Assert.False(_settings.CurrentValue.MagnifierEnabled);
        Assert.Equal("Magnifier off", _said[^1]);
    }

    [Fact]
    public void Start_WithTheSettingOn_TurnsItOnQuietly()
    {
        Set(s => s with { MagnifierEnabled = true });

        _controller.Start();

        Assert.True(_controller.IsOn);
        Assert.Empty(_said);
    }

    [Fact]
    public void WindowsMagnifierRunning_RefusesAndSaysWhy()
    {
        _magnifier.StartResult = MagnifierStartResult.WindowsMagnifierRunning;
        _controller.Start();

        Run(NavigationCommand.ToggleMagnifier);

        Assert.False(_controller.IsOn);
        Assert.False(_settings.CurrentValue.MagnifierEnabled);
        Assert.Contains("Windows Magnifier is running", Assert.Single(_said));
    }

    [Fact]
    public void PolicyOff_EveryCommandSaysSo_AndNothingStarts()
    {
        _controller.Dispose();
        _controller = Create(RunPolicy.Normal with { MagnifierOffByPolicy = true });
        Set(s => s with { MagnifierEnabled = true });
        _controller.Start();

        Run(NavigationCommand.ToggleMagnifier);
        Run(NavigationCommand.MagnifierZoomIn);

        Assert.False(_controller.IsAvailable);
        Assert.Equal(0, _magnifier.Starts);
        Assert.All(_said, s => Assert.Equal("Your organization has turned the magnifier off", s));
        Assert.Equal(2, _said.Count);
    }

    [Fact]
    public void SecureScreens_HaveNoMagnifier()
    {
        _controller.Dispose();
        _controller = Create(RunPolicy.Secure);

        Run(NavigationCommand.ToggleMagnifier);

        Assert.Equal(0, _magnifier.Starts);
        Assert.Equal("The magnifier isn't available on this screen", Assert.Single(_said));
    }

    [Fact]
    public void OtherCommands_AreNotTheMagnifiers() =>
        Assert.False(_controller.TryHandle(NavigationCommand.NextHeading));

    [Fact]
    public void ASettingsChange_TurnsItOnAndOff()
    {
        _controller.Start();

        // A profile for an application, the settings dialog or the wizard
        Set(s => s with { MagnifierEnabled = true });
        Assert.True(_controller.IsOn);

        Set(s => s with { MagnifierEnabled = false });
        Assert.False(_controller.IsOn);
    }

    [Fact]
    public void ARefusedStart_IsNotRetriedByOtherSettingsChanges()
    {
        _magnifier.StartResult = MagnifierStartResult.WindowsMagnifierRunning;
        Set(s => s with { MagnifierEnabled = true });
        _controller.Start();
        Assert.Single(_said);

        Set(s => s with { SpeechRateWpm = 250 });

        Assert.Equal(1, _magnifier.Starts);
        Assert.Single(_said);
    }

    [Fact]
    public void ScreenCurtain_WinsOverTheMagnifier_AndSaysSo()
    {
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier);
        _said.Clear();

        _controller.ScreenCurtainActive = true;

        Assert.False(_controller.IsOn);
        Assert.Contains("Screen curtain on", Assert.Single(_said));
        // The setting still says on: the magnifier comes back with the curtain gone
        Assert.True(_settings.CurrentValue.MagnifierEnabled);

        Run(NavigationCommand.ToggleMagnifier);
        Assert.False(_controller.IsOn);
        Assert.Contains("screen curtain is on", _said[^1]);

        _controller.ScreenCurtainActive = false;
        Assert.True(_controller.IsOn);
    }

    [Fact]
    public void Dispose_PutsTheScreenBack()
    {
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier);

        _controller.Dispose();

        Assert.False(_magnifier.IsRunning);
        Assert.Equal(1, _magnifier.Stops);
    }

    // -------------------------------------------------------------------------
    // Zoom, filters, views
    // -------------------------------------------------------------------------

    [Fact]
    public void ZoomIn_WhileOff_TurnsItOn()
    {
        _controller.Start();

        Run(NavigationCommand.MagnifierZoomIn);

        Assert.True(_controller.IsOn);
        Assert.Equal(200, _settings.CurrentValue.MagnifierZoomPercent);
    }

    [Fact]
    public void ZoomInAndOut_StepAndSay_AndKeepTheSetting()
    {
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier);

        Run(NavigationCommand.MagnifierZoomIn);
        Assert.Equal(250, _settings.CurrentValue.MagnifierZoomPercent);
        Assert.Equal(2.5, _magnifier.Last!.Zoom);
        Assert.Equal("2.5 times", _said[^1]);

        Run(NavigationCommand.MagnifierZoomOut);
        Run(NavigationCommand.MagnifierZoomOut);
        Assert.Equal(175, _settings.CurrentValue.MagnifierZoomPercent);
        Assert.Equal(1.75, _magnifier.Last!.Zoom);
    }

    [Fact]
    public void ZoomIn_AtSixteen_PlaysTheBoundaryCue()
    {
        Set(s => s with { MagnifierZoomPercent = 1600 });
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier);

        Run(NavigationCommand.MagnifierZoomIn);

        Assert.Equal(1600, _settings.CurrentValue.MagnifierZoomPercent);
        Assert.Equal("boundary", Assert.Single(_cues.Played));
    }

    [Fact]
    public void FullScreenAtOneTimes_ShowsTheScreenAsItIs()
    {
        Set(s => s with { MagnifierZoomPercent = 125 });
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier);
        _controller.FocusMoved(new ScreenRect(1800, 1000, 50, 20));

        Run(NavigationCommand.MagnifierZoomOut);

        Assert.Equal(1, _magnifier.Last!.Zoom);
        Assert.Equal((0.0, 0.0), (_magnifier.Last.Left, _magnifier.Last.Top));
    }

    [Fact]
    public void NextFilter_GoesRoundTheFilters_AndShowsEach()
    {
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier);

        Run(NavigationCommand.MagnifierNextFilter);
        Assert.Equal(MagnifierColorFilter.Inverted, _magnifier.Last!.Filter);
        Assert.Equal("Inverted colours", _said[^1]);

        for (int i = 0; i < 3; i++)
            Run(NavigationCommand.MagnifierNextFilter);
        Assert.Equal(MagnifierColorFilter.YellowOnBlack, _settings.CurrentValue.MagnifierColorFilter);
        Assert.Equal(MagnifierColorFilter.YellowOnBlack, _magnifier.Last!.Filter);

        Run(NavigationCommand.MagnifierNextFilter);
        Assert.Equal(MagnifierColorFilter.None, _magnifier.Last!.Filter);
    }

    [Fact]
    public void NextFilter_WhileOff_IsKeptForLater()
    {
        _controller.Start();

        Run(NavigationCommand.MagnifierNextFilter);

        Assert.Equal(MagnifierColorFilter.Inverted, _settings.CurrentValue.MagnifierColorFilter);
        Assert.Equal("Inverted colours, when the magnifier is on", Assert.Single(_said));
        Assert.Equal(0, _magnifier.Starts);
    }

    [Fact]
    public void NextView_Docked_ShowsAWindowAlongTheTop()
    {
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier);

        Run(NavigationCommand.MagnifierNextView);

        var frame = _magnifier.Last!;
        Assert.Equal(MagnifierView.Docked, frame.View);
        Assert.Equal(new ScreenRect(0, 0, 1920, 324), frame.Window);
        Assert.Equal("Docked", _said[^1]);
    }

    [Fact]
    public void MoveView_MovesTheDockedWindowToTheNextEdge()
    {
        Set(s => s with { MagnifierView = MagnifierView.Docked });
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier);

        Run(NavigationCommand.MagnifierMoveView);

        Assert.Equal(MagnifierDockEdge.Bottom, _settings.CurrentValue.MagnifierDockEdge);
        Assert.Equal(new ScreenRect(0, 756, 1920, 324), _magnifier.Last!.Window);
        Assert.Equal("Bottom", _said[^1]);
    }

    [Fact]
    public void MoveView_InFullScreen_SaysOnlyTheDockedViewMoves()
    {
        _controller.Start();

        Run(NavigationCommand.MagnifierMoveView);

        Assert.Contains("Only the docked view moves", Assert.Single(_said));
    }

    [Fact]
    public void LargerAndSmaller_ResizeTheDockedWindow_WithinItsLimits()
    {
        Set(s => s with { MagnifierView = MagnifierView.Docked, MagnifierDockedSizePercent = 75 });
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier);

        Run(NavigationCommand.MagnifierViewLarger);
        Assert.Equal(80, _settings.CurrentValue.MagnifierDockedSizePercent);
        Assert.Equal(864, _magnifier.Last!.Window!.Value.Height);
        Assert.Equal("80 percent", _said[^1]);

        Run(NavigationCommand.MagnifierViewLarger);
        Assert.Equal(80, _settings.CurrentValue.MagnifierDockedSizePercent);
        Assert.Equal("boundary", Assert.Single(_cues.Played));

        Run(NavigationCommand.MagnifierViewSmaller);
        Assert.Equal(75, _settings.CurrentValue.MagnifierDockedSizePercent);
    }

    [Fact]
    public void Resize_InFullScreen_SaysItIsTheWholeScreen()
    {
        _controller.Start();

        Run(NavigationCommand.MagnifierViewSmaller);

        Assert.Contains("whole screen", Assert.Single(_said));
    }

    [Fact]
    public void Lens_SitsOverWhatTheFocusIsOn()
    {
        Set(s => s with { MagnifierView = MagnifierView.Lens, MagnifierLensSizePercent = 20 });
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier);

        _controller.FocusMoved(new ScreenRect(390, 290, 20, 20));

        var frame = _magnifier.Last!;
        Assert.Equal(MagnifierView.Lens, frame.View);
        Assert.Equal(new ScreenRect(208, 192, 384, 216), frame.Window);
        // What is under the lens, magnified: 384×216 at 2× is 192×108 around the focus
        Assert.Equal((304.0, 246.0), (frame.Left, frame.Top));
    }

    // -------------------------------------------------------------------------
    // Panning
    // -------------------------------------------------------------------------

    [Fact]
    public void Pan_MovesAQuarterOfTheView_AndPlaysTheCueAtTheEdge()
    {
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier);
        var start = _magnifier.Last!;

        Run(NavigationCommand.MagnifierPanRight);
        Assert.Equal(start.Left + 240, _magnifier.Last!.Left);

        for (int i = 0; i < 4; i++)
            Run(NavigationCommand.MagnifierPanRight);
        Assert.Equal(960, _magnifier.Last!.Left);
        Assert.Contains("boundary", _cues.Played);
    }

    [Fact]
    public void Pan_WhileOff_SaysTheMagnifierIsOff()
    {
        _controller.Start();

        Run(NavigationCommand.MagnifierPanUp);

        Assert.Equal("Magnifier off", Assert.Single(_said));
    }

    // -------------------------------------------------------------------------
    // Tracking
    // -------------------------------------------------------------------------

    [Fact]
    public void Focus_OffTheView_IsCentred()
    {
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier);

        _controller.FocusMoved(new ScreenRect(100, 100, 40, 20));

        // Clamped at the screen's top-left corner
        Assert.Equal((0.0, 0.0), (_magnifier.Last!.Left, _magnifier.Last.Top));

        _controller.FocusMoved(new ScreenRect(1200, 700, 40, 20));
        Assert.Equal((1220 - 480.0, 710 - 270.0), (_magnifier.Last!.Left, _magnifier.Last.Top));
    }

    [Fact]
    public void Focus_AlreadyInView_DoesNotMoveIt()
    {
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier);
        int frames = _magnifier.Frames.Count;

        _controller.FocusMoved(new ScreenRect(900, 500, 40, 20));

        Assert.Equal(frames, _magnifier.Frames.Count);
    }

    [Fact]
    public void Focus_BeforeTurningOn_IsWhereTheViewStarts()
    {
        _controller.Start();
        _controller.FocusMoved(new ScreenRect(1600, 900, 20, 20));

        Run(NavigationCommand.ToggleMagnifier);

        Assert.Equal((1920 - 960.0, 1080 - 540.0), (_magnifier.Last!.Left, _magnifier.Last.Top));
    }

    [Fact]
    public void Caret_MovesTheViewOnlyAsFarAsItMust()
    {
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier); // 480..1440 across

        _controller.CaretMoved(new ScreenRect(1450, 500, 1, 18));

        // A tenth of the view's height (54) kept beyond the caret
        Assert.Equal(1451 + 54 - 960.0, _magnifier.Last!.Left);
        Assert.Equal(270, _magnifier.Last.Top);
    }

    [Fact]
    public void Mouse_IsFollowed_ExceptJustAfterAKeyboardMove()
    {
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier);
        _controller.FocusMoved(new ScreenRect(100, 100, 40, 20));
        var afterFocus = _magnifier.Last!;

        _controller.MouseMoved(1900, 1070);
        Assert.Equal(afterFocus, _magnifier.Last);

        _time.Advance(MagnifierController.MouseHoldOff);
        _controller.MouseMoved(1900, 1070);
        Assert.Equal((960.0, 540.0), (_magnifier.Last!.Left, _magnifier.Last.Top));
    }

    [Fact]
    public void ASourceTurnedOff_IsNotFollowed()
    {
        Set(s => s with { MagnifierFollowFocus = false, MagnifierFollowMouse = false });
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier);
        int frames = _magnifier.Frames.Count;

        _controller.FocusMoved(new ScreenRect(100, 100, 40, 20));
        _controller.MouseMoved(5, 5);

        Assert.Equal(frames, _magnifier.Frames.Count);
        Assert.False(_controller.Follows(MagnifierTrackingSource.Focus));
        Assert.True(_controller.Follows(MagnifierTrackingSource.Caret));
    }

    [Fact]
    public void Follows_IsFalseWhileOff()
    {
        _controller.Start();

        Assert.False(_controller.Follows(MagnifierTrackingSource.Mouse));
    }

    [Fact]
    public void SmoothTracking_GlidesThere()
    {
        Set(s => s with { MagnifierSmoothTracking = true });
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier);
        var start = _magnifier.Last!;

        _controller.FocusMoved(new ScreenRect(1600, 900, 20, 20));
        // Nothing jumps at once
        Assert.Equal(start, _magnifier.Last);

        _time.Advance(MagnifierController.GlideFrame);
        var first = _magnifier.Last!;
        Assert.True(first.Left > start.Left && first.Left < 960);

        _time.Advance(MagnifierController.GlideDuration);
        Assert.Equal((960.0, 540.0), (_magnifier.Last!.Left, _magnifier.Last.Top));
        Assert.Equal(0, _time.ActiveTimers);
    }

    [Fact]
    public void AMonitorAdded_IsUsedOnTheNextMove()
    {
        _controller.Start();
        Run(NavigationCommand.ToggleMagnifier);

        _displays.Displays = [new ScreenRect(0, 0, 1920, 1080), new ScreenRect(1920, 0, 1920, 1080)];
        _controller.FocusMoved(new ScreenRect(3800, 1000, 20, 20));

        Assert.Equal((3840 - 960.0, 540.0), (_magnifier.Last!.Left, _magnifier.Last.Top));
    }

    // -------------------------------------------------------------------------
    // The wizard's offer
    // -------------------------------------------------------------------------

    [Fact]
    public void Offer_TurnsItOn_OrSaysWhyNot()
    {
        IMagnifierOffer offer = _controller;
        _controller.ToggleKeyName = () => "Insert+Alt+Z";

        Assert.Equal("Insert+Alt+Z", offer.ToggleKey);
        Assert.Null(offer.TurnOn());
        Assert.True(_controller.IsOn);

        _controller.TurnOff();
        _magnifier.StartResult = MagnifierStartResult.Unavailable;
        Assert.Equal("The magnifier couldn't start", offer.TurnOn());
    }
}
