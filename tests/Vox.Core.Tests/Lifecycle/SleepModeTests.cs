using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Accessibility;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Lifecycle;

public class SleepModeTests : IDisposable
{
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private readonly Mock<IForegroundWindow> _foreground = new();
    private VoxSettings _settings = new();
    private readonly SleepMode _sleep;
    private readonly Dictionary<int, string> _processes = new() { [10] = "Notepad", [20] = "SelfVoicingGame" };
    private int _processLookups;

    public SleepModeTests()
    {
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        var monitor = new Mock<IOptionsMonitor<VoxSettings>>();
        monitor.Setup(m => m.CurrentValue).Returns(() => _settings);
        _sleep = new SleepMode(_foreground.Object, monitor.Object, s => _settings = s, _queue, pid =>
        {
            _processLookups++;
            return _processes.GetValueOrDefault(pid);
        });
        _queue.IsMuted = _sleep.IsAsleepNow;
        Focus(20);
    }

    public void Dispose() => _queue.Dispose();

    private void Focus(int processId) =>
        _foreground.Setup(f => f.Get()).Returns(new ForegroundWindowInfo(new IntPtr(processId), "Window", processId));

    /// <summary>Waits until everything queued so far has been spoken (the marker bypasses the mute gate).</summary>
    private async Task Drain()
    {
        var gate = _queue.IsMuted;
        _queue.IsMuted = null;
        _queue.Enqueue(new Utterance("end", SpeechPriority.Normal));
        _queue.IsMuted = gate;
        await _engine.WaitForTextAsync("end");
    }

    private async Task<IReadOnlyList<string>> SpokenAfter(Action action)
    {
        await Drain();
        _engine.Clear();
        action();
        await Drain();
        return _engine.SpokenText.Where(t => t != "end").ToList();
    }

    [Fact]
    public async Task Toggle_PutsTheFocusedAppToSleep_AndSaysSoFirst()
    {
        Assert.Equal(["Sleep mode on"], await SpokenAfter(() => Assert.True(_sleep.TryHandle(NavigationCommand.ToggleSleepMode))));

        Assert.Equal(["SelfVoicingGame"], _settings.SleepApps);
        Assert.True(_sleep.IsAsleepCached);
    }

    [Fact]
    public async Task WhileAsleep_NothingIsSaid()
    {
        _sleep.Toggle();

        Assert.Empty(await SpokenAfter(() => _queue.Enqueue(new Utterance("Button", SpeechPriority.Interrupt))));
    }

    [Fact]
    public async Task OtherApps_AreNotAsleep()
    {
        _sleep.Toggle();
        Focus(10);

        Assert.False(_sleep.IsAsleepNow());
        Assert.Equal(["Save, button"], await SpokenAfter(() => _queue.Enqueue(new Utterance("Save, button", SpeechPriority.Interrupt))));
    }

    [Fact]
    public async Task ToggleAgain_Wakes()
    {
        _sleep.Toggle();

        Assert.Equal(["Sleep mode off"], await SpokenAfter(_sleep.Toggle));
        Assert.Empty(_settings.SleepApps);
        Assert.False(_sleep.IsAsleepCached);
    }

    [Fact]
    public void ProcessNames_AreLookedUpOncePerProcess()
    {
        _sleep.IsAsleepNow();
        _sleep.IsAsleepNow();
        _sleep.IsAsleepNow();

        Assert.Equal(1, _processLookups);
    }

    [Fact]
    public void NoForegroundWindow_IsNotAsleep()
    {
        _foreground.Setup(f => f.Get()).Returns((ForegroundWindowInfo?)null);

        Assert.False(_sleep.IsAsleepNow());
    }
}
