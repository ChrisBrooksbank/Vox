using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Accessibility;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;
using TextUnit = Vox.Core.Text.TextUnit;

namespace Vox.Core.Tests.Accessibility;

public class MouseTrackerTests : IDisposable
{
    private readonly UIAThread _uiaThread = new(NullLogger<UIAThread>.Instance);
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private readonly Pointer _pointer = new();
    private readonly Targets _targets = new();
    private readonly Mock<IOptionsMonitor<VoxSettings>> _settings = new();
    private VoxSettings _current = new() { MouseTracking = true, MouseTextUnit = MouseTextUnit.Object };
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly MouseTracker _tracker;

    private sealed class Pointer : IMousePointer
    {
        public (int X, int Y)? Position { get; set; } = (0, 0);
        public (int X, int Y)? GetPosition() => Position;
    }

    private sealed class Targets : IPointerTargetSource
    {
        public Dictionary<(int, int), PointerTarget> At { get; } = new();
        public List<(int X, int Y, TextUnit? Unit)> Lookups { get; } = new();

        PointerTarget? IPointerTargetSource.At(int x, int y, TextUnit? unit)
        {
            Lookups.Add((x, y, unit));
            if (!At.TryGetValue((x, y), out var target))
                return null;
            return unit is null ? target with { Text = null } : target;
        }
    }

    public MouseTrackerTests()
    {
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        _settings.Setup(s => s.CurrentValue).Returns(() => _current);
        _tracker = new MouseTracker(_uiaThread, _pointer, _targets, _queue, new AnnouncementBuilder(), _settings.Object,
            s => _current = s, NullLogger<MouseTracker>.Instance, () => _now);

        _targets.At[(10, 10)] = new PointerTarget(Object("OK", "Button", 1), null);
        _targets.At[(20, 20)] = new PointerTarget(Object("Cancel", "Button", 2), null);
        _targets.At[(30, 30)] = new PointerTarget(Object("Text editor", "Edit", 3), "Dear Sam");
        _targets.At[(31, 31)] = new PointerTarget(Object("Text editor", "Edit", 3), "Dear Sam");
    }

    public void Dispose()
    {
        _tracker.Dispose();
        _queue.Dispose();
        _uiaThread.Dispose();
    }

    private static FocusChangedEvent Object(string name, string controlType, int id) =>
        new(DateTimeOffset.UtcNow, name, controlType, RuntimeId: [42, id]);

    /// <summary>Moves the pointer, lets the throttle interval pass, and ticks.</summary>
    private async Task MoveTo(int x, int y)
    {
        _pointer.Position = (x, y);
        _now += MouseTracker.LookupInterval;
        await _tracker.TickAsync();
    }

    /// <summary>Moves the pointer there and returns what was said.</summary>
    private async Task<string> Hear(int x, int y)
    {
        _engine.Clear();
        await MoveTo(x, y);
        return (await _engine.WaitForAsync(_ => true)).Text;
    }

    /// <summary>Everything said since the last clear, once the queue has caught up.</summary>
    private async Task<IReadOnlyList<string>> Spoken()
    {
        _queue.Enqueue(new Utterance("end", SpeechPriority.Normal));
        await _engine.WaitForTextAsync("end");
        return _engine.SpokenText.Where(t => t != "end").ToList();
    }

    [Fact]
    public async Task SpeaksTheObjectUnderThePointer_WhenItChanges()
    {
        await _tracker.TickAsync(); // starts tracking where the pointer is
        Assert.Contains("OK", await Hear(10, 10));
        Assert.Contains("Cancel", await Hear(20, 20));
    }

    [Fact]
    public async Task NothingIsSaid_UntilThePointerMoves()
    {
        _pointer.Position = (10, 10);
        await _tracker.TickAsync();
        _now += TimeSpan.FromSeconds(1);
        await _tracker.TickAsync();

        Assert.Empty(_targets.Lookups);
        Assert.Empty(await Spoken());
    }

    [Fact]
    public async Task SameObject_IsNotRepeated()
    {
        await _tracker.TickAsync();
        await Hear(10, 10);
        _engine.Clear();
        _targets.At[(11, 11)] = _targets.At[(10, 10)];
        await MoveTo(11, 11);

        Assert.Equal(2, _targets.Lookups.Count);
        Assert.Empty(await Spoken());
    }

    [Fact]
    public async Task Lookups_AreThrottled()
    {
        await _tracker.TickAsync();
        _pointer.Position = (10, 10);
        _now += MouseTracker.LookupInterval;
        await _tracker.TickAsync();
        // Moves within the interval wait for it
        _pointer.Position = (20, 20);
        _now += TimeSpan.FromMilliseconds(50);
        await _tracker.TickAsync();
        Assert.Single(_targets.Lookups);

        _now += TimeSpan.FromMilliseconds(50);
        await _tracker.TickAsync();
        Assert.Equal(2, _targets.Lookups.Count);
        Assert.Equal((20, 20), (_targets.Lookups[1].X, _targets.Lookups[1].Y));
    }

    [Fact]
    public async Task Off_NoLookups()
    {
        _current = _current with { MouseTracking = false };
        await _tracker.TickAsync();
        await MoveTo(10, 10);
        await MoveTo(20, 20);

        Assert.Empty(_targets.Lookups);
    }

    [Fact]
    public async Task LineUnit_SpeaksTheTextUnderThePointer_OnceForTheSameText()
    {
        _current = _current with { MouseTextUnit = MouseTextUnit.Line };
        await _tracker.TickAsync();
        Assert.Equal("Dear Sam", await Hear(30, 30));
        Assert.Equal(TextUnit.Line, _targets.Lookups[0].Unit);

        _engine.Clear();
        await MoveTo(31, 31);
        Assert.Empty(await Spoken());

        // No text under the pointer: the object is said
        Assert.Contains("OK", await Hear(10, 10));
    }

    [Fact]
    public async Task ObjectUnit_DoesNotAskForText()
    {
        await _tracker.TickAsync();
        Assert.Contains("Text editor", await Hear(30, 30));
        Assert.Null(_targets.Lookups[0].Unit);
    }

    [Fact]
    public async Task ToggleCommand_FlipsTheSetting()
    {
        Assert.True(_tracker.TryHandle(NavigationCommand.ToggleMouseTracking));
        Assert.False(_current.MouseTracking);
        await _engine.WaitForTextAsync("Mouse tracking off");

        Assert.True(_tracker.TryHandle(NavigationCommand.ToggleMouseTracking));
        Assert.True(_current.MouseTracking);
        await _engine.WaitForTextAsync("Mouse tracking on");

        Assert.False(_tracker.TryHandle(NavigationCommand.SayAll));
    }
}
