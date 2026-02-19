using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Audio;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Xunit;

namespace Vox.Core.Tests.Pipeline;

public class EventPipelineTests : IDisposable
{
    private readonly Mock<ISpeechEngine> _engineMock;
    private readonly Mock<IAudioCuePlayer> _audioCueMock;
    private readonly SpeechQueue _speechQueue;
    private readonly EventPipeline _pipeline;

    private readonly List<Utterance> _spokenUtterances = new();
    private readonly List<string> _playedCues = new();

    public EventPipelineTests()
    {
        _engineMock = new Mock<ISpeechEngine>();
        _audioCueMock = new Mock<IAudioCuePlayer>();

        // Capture spoken utterances
        _engineMock
            .Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Returns((Utterance u, CancellationToken _) =>
            {
                lock (_spokenUtterances) _spokenUtterances.Add(u);
                return Task.CompletedTask;
            });

        // Capture played audio cues
        _audioCueMock
            .Setup(c => c.Play(It.IsAny<string>()))
            .Callback((string name) =>
            {
                lock (_playedCues) _playedCues.Add(name);
            });
        _audioCueMock.SetupGet(c => c.IsEnabled).Returns(true);

        _speechQueue = new SpeechQueue(_engineMock.Object, NullLogger<SpeechQueue>.Instance);
        _pipeline = new EventPipeline(_speechQueue, _audioCueMock.Object, NullLogger<EventPipeline>.Instance);
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        _speechQueue.Dispose();
    }

    // -------------------------------------------------------------------------
    // Coalescing: consecutive focus events within 30ms keep only the last
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FocusCoalescing_ConsecutiveFocusEvents_KeepsOnlyLast()
    {
        var collectedFocusEvents = new List<FocusChangedEvent>();
        _pipeline.FocusChangedProcessed += (_, e) =>
        {
            lock (collectedFocusEvents) collectedFocusEvents.Add(e);
        };

        // Post 3 focus events in rapid succession (well within 30ms)
        var now = DateTimeOffset.UtcNow;
        var evt1 = new FocusChangedEvent(now, "Button One", "Button");
        var evt2 = new FocusChangedEvent(now, "Button Two", "Button");
        var evt3 = new FocusChangedEvent(now, "Button Three", "Button");

        _pipeline.Post(evt1);
        _pipeline.Post(evt2);
        _pipeline.Post(evt3);

        // Wait longer than the 30ms coalescing window plus processing
        await Task.Delay(200);

        lock (collectedFocusEvents)
        {
            // Only the last focus event should have been processed
            Assert.Single(collectedFocusEvents);
            Assert.Equal("Button Three", collectedFocusEvents[0].ElementName);
        }
    }

    [Fact]
    public async Task FocusCoalescing_FocusFollowedByNonFocus_BothProcessed()
    {
        var collectedFocusEvents = new List<FocusChangedEvent>();
        _pipeline.FocusChangedProcessed += (_, e) =>
        {
            lock (collectedFocusEvents) collectedFocusEvents.Add(e);
        };

        var now = DateTimeOffset.UtcNow;
        // Post a focus event followed immediately by a navigation command
        _pipeline.Post(new FocusChangedEvent(now, "Link", "Hyperlink"));
        _pipeline.Post(new NavigationCommandEvent(now, NavigationCommand.StopSpeech));

        await Task.Delay(200);

        lock (collectedFocusEvents)
        {
            // The focus event should still have been processed
            Assert.Single(collectedFocusEvents);
            Assert.Equal("Link", collectedFocusEvents[0].ElementName);
        }
    }

    [Fact]
    public async Task FocusCoalescing_SingleFocusEvent_IsProcessed()
    {
        var collectedFocusEvents = new List<FocusChangedEvent>();
        _pipeline.FocusChangedProcessed += (_, e) =>
        {
            lock (collectedFocusEvents) collectedFocusEvents.Add(e);
        };

        var now = DateTimeOffset.UtcNow;
        _pipeline.Post(new FocusChangedEvent(now, "TextBox", "Edit"));

        await Task.Delay(200);

        lock (collectedFocusEvents)
        {
            Assert.Single(collectedFocusEvents);
            Assert.Equal("TextBox", collectedFocusEvents[0].ElementName);
        }
    }

    // -------------------------------------------------------------------------
    // Priority routing: assertive live region -> High priority
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LiveRegion_Assertive_EnqueuesHighPriority()
    {
        var now = DateTimeOffset.UtcNow;
        _pipeline.Post(new LiveRegionChangedEvent(now, "Alert: Error occurred", LiveRegionPoliteness.Assertive));

        await Task.Delay(200);

        lock (_spokenUtterances)
        {
            var utterance = _spokenUtterances.FirstOrDefault(u => u.Text.Contains("Alert"));
            Assert.NotNull(utterance);
            Assert.Equal(SpeechPriority.High, utterance!.Priority);
        }
    }

    [Fact]
    public async Task LiveRegion_Polite_EnqueuesLowPriority()
    {
        var now = DateTimeOffset.UtcNow;
        _pipeline.Post(new LiveRegionChangedEvent(now, "Status updated", LiveRegionPoliteness.Polite));

        await Task.Delay(200);

        lock (_spokenUtterances)
        {
            var utterance = _spokenUtterances.FirstOrDefault(u => u.Text.Contains("Status"));
            Assert.NotNull(utterance);
            Assert.Equal(SpeechPriority.Low, utterance!.Priority);
        }
    }

    // -------------------------------------------------------------------------
    // Priority routing: mode change -> audio cue first, then speech
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ModeChanged_ToBrowse_PlaysAudioCueBeforeSpeech()
    {
        var cueOrder = new List<string>();
        var speechOrder = new List<string>();

        _audioCueMock
            .Setup(c => c.Play(It.IsAny<string>()))
            .Callback((string name) =>
            {
                lock (cueOrder) cueOrder.Add($"cue:{name}");
                lock (speechOrder) speechOrder.Add($"cue:{name}");
            });

        _engineMock
            .Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Returns((Utterance u, CancellationToken _) =>
            {
                lock (speechOrder) speechOrder.Add($"speech:{u.Text}");
                return Task.CompletedTask;
            });

        var now = DateTimeOffset.UtcNow;
        _pipeline.Post(new ModeChangedEvent(now, InteractionMode.Browse));

        await Task.Delay(300);

        // Verify audio cue was played
        lock (cueOrder)
        {
            Assert.Contains(cueOrder, c => c.Contains("browse_mode"));
        }

        // Verify audio cue came before speech in ordering
        lock (speechOrder)
        {
            var cueIdx = speechOrder.FindIndex(s => s.StartsWith("cue:"));
            var speechIdx = speechOrder.FindIndex(s => s.StartsWith("speech:") && s.Contains("Browse"));
            Assert.True(cueIdx >= 0, "Audio cue should have been played");
            Assert.True(speechIdx >= 0, "Browse mode speech should have been spoken");
            Assert.True(cueIdx < speechIdx, "Audio cue should come before speech announcement");
        }
    }

    [Fact]
    public async Task ModeChanged_ToFocus_PlaysFocusModeCue()
    {
        var now = DateTimeOffset.UtcNow;
        _pipeline.Post(new ModeChangedEvent(now, InteractionMode.Focus));

        await Task.Delay(300);

        _audioCueMock.Verify(c => c.Play("focus_mode"), Times.Once);
    }

    [Fact]
    public async Task ModeChanged_ToBrowse_EnqueuesInterruptPrioritySpeech()
    {
        var now = DateTimeOffset.UtcNow;
        _pipeline.Post(new ModeChangedEvent(now, InteractionMode.Browse));

        await Task.Delay(300);

        lock (_spokenUtterances)
        {
            var utterance = _spokenUtterances.FirstOrDefault(u => u.Text.Contains("Browse"));
            Assert.NotNull(utterance);
            Assert.Equal(SpeechPriority.Interrupt, utterance!.Priority);
        }
    }
}
