using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Audio;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Pipeline;

/// <summary>The pipeline's hooks for app modules: the event filter, the focus formatter and EventProcessed.</summary>
public class EventPipelineAppModuleTests : IDisposable
{
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _queue;
    private readonly EventPipeline _pipeline;

    public EventPipelineAppModuleTests()
    {
        _queue = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        _pipeline = new EventPipeline(_queue, new Mock<IAudioCuePlayer>().Object, NullLogger<EventPipeline>.Instance);
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        _queue.Dispose();
    }

    private static FocusChangedEvent Focus(string name) => new(DateTimeOffset.UtcNow, name, "Button");

    /// <summary>Waits until everything posted before has been handled.</summary>
    private async Task DrainAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pipeline.Post(new PipelineWorkEvent(DateTimeOffset.UtcNow, () => done.SetResult()));
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task EventFilter_DroppedEvent_IsNeitherSpokenNorRaised()
    {
        int raised = 0;
        _pipeline.FocusChangedProcessed += (_, _) => raised++;
        _pipeline.EventFilter = evt => evt is not FocusChangedEvent { ElementName: "Hidden" };

        _pipeline.Post(Focus("Hidden"));
        await DrainAsync();
        _pipeline.Post(Focus("Shown"));
        await _engine.WaitForTextAsync("Shown, button");

        Assert.Equal(1, raised);
        Assert.DoesNotContain(_engine.SpokenText, t => t.Contains("Hidden"));
    }

    [Fact]
    public async Task EventFilter_NeverSeesPipelineWork()
    {
        _pipeline.EventFilter = _ => false;
        bool ran = false;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pipeline.Post(new PipelineWorkEvent(DateTimeOffset.UtcNow, () => { ran = true; done.SetResult(); }));

        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(ran);
    }

    [Fact]
    public async Task FocusAnnouncementFormatter_ReplacesTheAnnouncement()
    {
        _pipeline.FocusAnnouncementFormatter = (focus, text) => $"{text}, from {focus.ElementName}";

        _pipeline.Post(Focus("OK"));

        await _engine.WaitForTextAsync("OK, button, from OK");
    }

    [Fact]
    public async Task FocusAnnouncementFormatter_Null_SaysNothing_ButTheContextIsStillSaid()
    {
        _pipeline.FocusAnnouncementFormatter = (_, _) => null;
        _pipeline.FocusContextProvider = focus => focus.ElementName == "Second" ? "Window title" : null;

        _pipeline.Post(Focus("First"));
        await DrainAsync();
        await Task.Delay(50);
        Assert.Empty(_engine.SpokenText);

        _pipeline.Post(Focus("Second"));
        var spoken = await _engine.WaitForTextAsync("Window title");
        Assert.Equal(SpeechPriority.Interrupt, spoken.Priority);
    }

    [Fact]
    public async Task EventProcessed_IsRaisedAfterTheEventIsHandled()
    {
        var order = new List<string>();
        _pipeline.FocusChangedProcessed += (_, _) => order.Add("focus");
        _pipeline.EventProcessed += (_, evt) => order.Add($"processed {evt.GetType().Name}");

        _pipeline.Post(Focus("OK"));
        _pipeline.Post(new ToolTipOpenedEvent(DateTimeOffset.UtcNow, "Tip"));
        await DrainAsync();

        Assert.Equal(["focus", "processed FocusChangedEvent", "processed ToolTipOpenedEvent"], order);
    }
}
