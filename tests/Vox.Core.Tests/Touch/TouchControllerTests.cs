using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Accessibility;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Vox.Core.Touch;
using Xunit;
using TextUnit = Vox.Core.Text.TextUnit;

namespace Vox.Core.Tests.Touch;

public class TouchControllerTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class FakeSource : ITouchSource
    {
        public bool Started { get; private set; }
        public bool CanStart { get; set; } = true;
        public event Action<TouchPoint>? PointReceived;
        public bool Start() => Started = CanStart;
        public void Stop() => Started = false;
        public void Send(TouchPoint p) => PointReceived?.Invoke(p);
        public void Dispose() { }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = T0;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Targets : IPointerTargetSource
    {
        public Dictionary<(int, int), PointerTarget> At { get; } = new();
        PointerTarget? IPointerTargetSource.At(int x, int y, TextUnit? unit) => At.TryGetValue((x, y), out var t) ? t : null;
    }

    private sealed class Activator : ITouchActivator
    {
        public List<(int X, int Y)> Activated { get; } = [];
        public bool ActivateAt(int x, int y) { Activated.Add((x, y)); return true; }
    }

    private sealed class Sink : IEventSink
    {
        public List<NavigationCommand> Commands { get; } = [];
        public void Post(ScreenReaderEvent evt)
        {
            if (evt is NavigationCommandEvent c)
                lock (Commands) Commands.Add(c.Command);
        }
        public NavigationCommand[] Snapshot() { lock (Commands) return [.. Commands]; }
    }

    private readonly UIAThread _uiaThread = new(NullLogger<UIAThread>.Instance);
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private readonly FakeSource _source = new();
    private readonly Targets _targets = new();
    private readonly Activator _activator = new();
    private readonly Sink _sink = new();
    private readonly Clock _clock = new();
    private readonly Mock<IOptionsMonitor<VoxSettings>> _settings = new();
    private VoxSettings _current = new() { TouchEnabled = true };
    private TouchContext _context = new(false, false);
    private RunPolicy _policy = RunPolicy.Normal;
    private TouchController? _controller;

    public TouchControllerTests()
    {
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        _settings.Setup(s => s.CurrentValue).Returns(() => _current);
        _targets.At[(10, 10)] = new PointerTarget(new FocusChangedEvent(T0, "OK", "Button", RuntimeId: [42, 1]), null);
        _targets.At[(20, 20)] = new PointerTarget(new FocusChangedEvent(T0, "Cancel", "Button", RuntimeId: [42, 2]), null);
    }

    private TouchController Controller()
    {
        // One pixel is one millimetre
        _controller = new TouchController(_source, _targets, _activator, _uiaThread, _queue, new AnnouncementBuilder(),
            _settings.Object, s => _current = s, _sink, _policy, () => _context, NullLogger<TouchController>.Instance,
            time: _clock, pixelsPerMm: 1);
        _controller.Refresh();
        return _controller;
    }

    public void Dispose()
    {
        _controller?.Dispose();
        _queue.Dispose();
        _uiaThread.Dispose();
    }

    private static TouchPoint P(int id, int x, int y, TouchPhase phase, int ms) => new(id, x, y, phase, T0.AddMilliseconds(ms));

    private async Task Explore(TouchController c, int x, int y)
    {
        _source.Send(P(1, x - 6, y, TouchPhase.Down, 0));
        _source.Send(P(1, x - 3, y, TouchPhase.Move, 100));
        _source.Send(P(1, x, y, TouchPhase.Move, 200));
        await c.ExploreIdle;
    }

    private void Flick(int fingers, int dx, int dy)
    {
        for (int i = 0; i < fingers; i++)
            _source.Send(P(i, 200, 200 + i * 30, TouchPhase.Down, i * 5));
        for (int i = 0; i < fingers; i++)
            _source.Send(P(i, 200 + dx, 200 + i * 30 + dy, TouchPhase.Move, 50));
        for (int i = 0; i < fingers; i++)
            _source.Send(P(i, 200 + dx, 200 + i * 30 + dy, TouchPhase.Up, 60));
    }

    private void Tap(int fingers, int startMs = 0)
    {
        for (int i = 0; i < fingers; i++)
            _source.Send(P(i, 100, 100 + i * 30, TouchPhase.Down, startMs + i * 5));
        for (int i = 0; i < fingers; i++)
            _source.Send(P(i, 100, 100 + i * 30, TouchPhase.Up, startMs + 60 + i * 5));
    }

    [Fact]
    public async Task Exploring_SaysTheObjectOnce()
    {
        var c = Controller();
        await Explore(c, 10, 10);
        await _engine.WaitForTextAsync("OK, button", TimeSpan.FromSeconds(3));
        // Moving on the same object says nothing more
        _source.Send(P(1, 10, 10, TouchPhase.Move, 400));
        await c.ExploreIdle;
        Assert.Single(_engine.SpokenText, t => t.Contains("OK"));
    }

    [Fact]
    public async Task Exploring_SaysTheNextObjectWhenFingerMovesOnto()
    {
        var c = Controller();
        await Explore(c, 10, 10);
        _source.Send(P(1, 15, 15, TouchPhase.Move, 300));
        _source.Send(P(1, 20, 20, TouchPhase.Move, 400));
        await c.ExploreIdle;
        await _engine.WaitForTextAsync("Cancel, button", TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void FlicksOutsideBrowseMode_MoveTheNavigatorObject()
    {
        var c = Controller();
        Flick(1, 40, 0);
        Assert.Equal([NavigationCommand.TouchNextItem], _sink.Snapshot());
        Assert.True(c.TryHandle(NavigationCommand.TouchNextItem));
        Assert.True(c.TryHandle(NavigationCommand.TouchPreviousItem));
        Assert.Equal([NavigationCommand.TouchNextItem, NavigationCommand.NavigatorNext, NavigationCommand.NavigatorPrevious], _sink.Snapshot());
    }

    [Fact]
    public async Task InBrowseMode_FlicksFollowTheRotor_ThreeFingerFlicksChooseIt()
    {
        var c = Controller();
        _context = new TouchContext(true, true);
        c.TryHandle(NavigationCommand.TouchNextItem);
        Assert.Equal(NavigationCommand.NextHeading, _sink.Snapshot()[^1]);

        Flick(3, 60, 0);
        Assert.Equal(NavigationCommand.TouchNextRotorSetting, _sink.Snapshot()[^1]);
        c.TryHandle(NavigationCommand.TouchNextRotorSetting);
        await _engine.WaitForTextAsync("Links", TimeSpan.FromSeconds(3));
        c.TryHandle(NavigationCommand.TouchPreviousItem);
        Assert.Equal(NavigationCommand.PrevLink, _sink.Snapshot()[^1]);

        c.TryHandle(NavigationCommand.TouchPreviousRotorSetting);
        c.TryHandle(NavigationCommand.TouchPreviousRotorSetting);
        c.TryHandle(NavigationCommand.TouchNextItem); // wrapped to lines
        Assert.Equal(NavigationCommand.NextLine, _sink.Snapshot()[^1]);
    }

    [Fact]
    public void TwoFingerTap_StopsSpeech_DoubleTapActivates()
    {
        Controller();
        Tap(2);
        Assert.Equal([NavigationCommand.StopSpeech], _sink.Snapshot());
        Tap(1, 1000);
        Tap(1, 1200);
        Assert.Equal(NavigationCommand.TouchActivate, _sink.Snapshot()[^1]);
    }

    [Fact]
    public async Task Activate_ClicksTheLastExploredPoint_OrTheNavigatorWhenNothingWasExplored()
    {
        var c = Controller();
        c.TryHandle(NavigationCommand.TouchActivate);
        Assert.Equal([NavigationCommand.ActivateNavigator], _sink.Snapshot());

        await Explore(c, 10, 10);
        c.TryHandle(NavigationCommand.TouchActivate);
        await Task.Delay(300);
        Assert.Equal([(10, 10)], _activator.Activated);

        _context = new TouchContext(true, true);
        c.TryHandle(NavigationCommand.TouchActivate);
        Assert.Equal(NavigationCommand.ActivateElement, _sink.Snapshot()[^1]);
    }

    [Fact]
    public void Disabled_IgnoresTouch_AndNeverStartsTheSource()
    {
        _current = new VoxSettings { TouchEnabled = false };
        Controller();
        Assert.False(_source.Started);
        Flick(1, 40, 0);
        Assert.Empty(_sink.Snapshot());
    }

    [Fact]
    public void SecureScreens_NeverStartTouch()
    {
        _policy = RunPolicy.Secure;
        var c = Controller();
        Assert.False(c.IsRunning);
        Assert.False(_source.Started);
    }

    [Fact]
    public void TurningTheSettingOff_StopsCapture()
    {
        var c = Controller();
        Assert.True(_source.Started);
        _current = _current with { TouchEnabled = false };
        c.Refresh();
        Assert.False(_source.Started);
        Assert.False(c.IsRunning);
    }

    [Fact]
    public async Task ToggleTouch_ChangesTheSettingAndSaysSo()
    {
        var c = Controller();
        Assert.True(c.TryHandle(NavigationCommand.ToggleTouch));
        Assert.False(_current.TouchEnabled);
        await _engine.WaitForTextAsync("Touch off", TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task SourceThatCannotStart_SaysSo()
    {
        _source.CanStart = false;
        var c = Controller();
        Assert.False(c.IsRunning);
        await _engine.WaitForTextAsync("Touch could not be turned on", TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void NonTouchCommands_AreNotHandled()
    {
        var c = Controller();
        Assert.False(c.TryHandle(NavigationCommand.NextHeading));
    }
}
