using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Input;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class SpeechOnDemandTests : IDisposable
{
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private long _now = 1000;
    private bool _on = true;

    public SpeechOnDemandTests()
    {
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        _queue.OnDemand = new SpeechOnDemand(() => _now) { IsOnFunc = () => _on };
    }

    public void Dispose() => _queue.Dispose();

    [Fact]
    public async Task WhenOn_UnmarkedSpeechIsDropped()
    {
        _queue.Enqueue(new Utterance("focus changed", SpeechPriority.Interrupt));
        _queue.Enqueue(new Utterance("marker", SpeechPriority.Interrupt) { OnDemand = true });
        await _engine.WaitForTextAsync("marker");
        Assert.DoesNotContain("focus changed", _engine.SpokenText);
    }

    [Fact]
    public async Task WhenOff_EverythingIsSpoken()
    {
        _on = false;
        _queue.Enqueue(new Utterance("focus changed", SpeechPriority.Interrupt));
        await _engine.WaitForTextAsync("focus changed");
    }

    [Fact]
    public async Task ReportingCommand_MarksTheSpeechThatFollows()
    {
        _queue.OnDemand.CommandReceived(NavigationCommand.SayTime);
        _queue.Enqueue(new Utterance("10:30", SpeechPriority.Interrupt));
        await _engine.WaitForTextAsync("10:30");
    }

    [Fact]
    public void ReportingCommand_WindowEnds()
    {
        _queue.OnDemand.CommandReceived(NavigationCommand.SayTitle);
        Assert.True(_queue.OnDemand.IsOpen);
        _now += (long)SpeechOnDemand.Window.TotalMilliseconds + 1;
        Assert.False(_queue.OnDemand.IsOpen);
    }

    [Fact]
    public void OtherCommand_ClosesTheWindow()
    {
        _queue.OnDemand.CommandReceived(NavigationCommand.SayTitle);
        _queue.OnDemand.CommandReceived(NavigationCommand.NextHeading);
        Assert.False(_queue.OnDemand.IsOpen);
    }

    [Fact]
    public void SayAll_StaysOpenUntilAnotherCommand()
    {
        _queue.OnDemand.CommandReceived(NavigationCommand.SayAll);
        _now += 10 * 60 * 1000;
        Assert.True(_queue.OnDemand.IsOpen);
        _queue.OnDemand.CommandReceived(NavigationCommand.StopSpeech);
        Assert.False(_queue.OnDemand.IsOpen);
    }

    [Fact]
    public async Task AwaitedUtterance_IsCancelledWhenDropped()
    {
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => _queue.EnqueueAndWaitAsync(new Utterance("line", SpeechPriority.Normal)));
    }

    [Fact]
    public async Task DroppedSpeech_StillRaisesUtteranceQueued()
    {
        var seen = new List<string>();
        _queue.UtteranceQueued += (_, u) => seen.Add(u.Text);
        _queue.Enqueue(new Utterance("for braille", SpeechPriority.Normal));
        await Task.Yield();
        Assert.Contains("for braille", seen);
    }

    [Fact]
    public void EveryCommand_HasADecision_AndReportsAreCatalogued()
    {
        foreach (var command in Enum.GetValues<NavigationCommand>())
        {
            var report = CommandCatalog.IsOnDemandReport(command);
            // A report must be a described command; the decision is explicit either way
            if (report) Assert.True(CommandCatalog.Has(command), command.ToString());
        }
        Assert.True(CommandCatalog.IsOnDemandReport(NavigationCommand.SayTime));
        Assert.True(CommandCatalog.IsOnDemandReport(NavigationCommand.ReadSelection));
        Assert.False(CommandCatalog.IsOnDemandReport(NavigationCommand.NextHeading));
        Assert.False(CommandCatalog.IsOnDemandReport(NavigationCommand.ToggleMode));
    }
}
