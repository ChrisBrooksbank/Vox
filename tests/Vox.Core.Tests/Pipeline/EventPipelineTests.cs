using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Audio;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
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
        var engine = new RecordingSpeechEngine();
        using var queue = new SpeechQueue(engine, NullLogger<SpeechQueue>.Instance);
        using var pipeline = new EventPipeline(queue, _audioCueMock.Object, NullLogger<EventPipeline>.Instance);

        pipeline.Post(new LiveRegionChangedEvent(DateTimeOffset.UtcNow, "Alert: Error occurred", LiveRegionPoliteness.Assertive));

        var spoken = await engine.WaitForTextAsync("Alert: Error occurred");
        Assert.Equal(SpeechPriority.High, spoken.Priority);
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
    public async Task ModeChanged_ToBrowse_IsQueuedNotInterrupting()
    {
        var now = DateTimeOffset.UtcNow;
        _pipeline.Post(new ModeChangedEvent(now, InteractionMode.Browse));

        await Task.Delay(300);

        lock (_spokenUtterances)
        {
            var utterance = _spokenUtterances.FirstOrDefault(u => u.Text.Contains("Browse"));
            Assert.NotNull(utterance);
            // Queued after whatever is being said (e.g. a focus announcement), never cutting it off
            Assert.Equal(SpeechPriority.High, utterance!.Priority);
        }
    }

    [Fact]
    public async Task ModeChanged_Automatic_PlaysCueWithoutSpeech()
    {
        _pipeline.Post(new ModeChangedEvent(DateTimeOffset.UtcNow, InteractionMode.Focus, "focus moved to edit field", Announce: false));

        await Task.Delay(300);

        lock (_playedCues) Assert.Contains("focus_mode", _playedCues);
        lock (_spokenUtterances) Assert.DoesNotContain(_spokenUtterances, u => u.Text.Contains("Focus mode"));
    }

    [Fact]
    public async Task FocusOnEditField_AnnouncementIsNotCutOffByAutomaticModeSwitch()
    {
        // What the controller does inside FocusChangedProcessed: switch mode automatically
        _pipeline.FocusChangedProcessed += (_, _) =>
            _pipeline.Post(new ModeChangedEvent(DateTimeOffset.UtcNow, InteractionMode.Focus, Announce: false));

        _pipeline.Post(new FocusChangedEvent(DateTimeOffset.UtcNow, "Search", "Edit"));

        await Task.Delay(300);

        _engineMock.Verify(e => e.Cancel(), Times.Once); // only the focus announcement's own interrupt
        lock (_spokenUtterances)
        {
            Assert.Contains(_spokenUtterances, u => u.Text == "Search, edit");
            Assert.DoesNotContain(_spokenUtterances, u => u.Text.Contains("Focus mode"));
        }
    }

    [Fact]
    public async Task ImportantNotification_FromBackgroundProcess_IsSpoken()
    {
        _pipeline.Post(new NotificationEvent(DateTimeOffset.UtcNow, "toast", "New message from Sam", Processing: 0, IsFromForeground: false));

        await Task.Delay(300);

        lock (_spokenUtterances) Assert.Contains(_spokenUtterances, u => u.Text == "New message from Sam");
    }

    // -------------------------------------------------------------------------
    // Live region filtering and browse events
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LiveRegion_Off_IsNotSpoken()
    {
        _pipeline.Post(new LiveRegionChangedEvent(DateTimeOffset.UtcNow, "Hidden update", LiveRegionPoliteness.Off));

        await Task.Delay(200);

        lock (_spokenUtterances)
            Assert.DoesNotContain(_spokenUtterances, u => u.Text.Contains("Hidden"));
    }

    [Fact]
    public async Task LiveRegion_SameTextFromSameSource_IsSpokenOnce()
    {
        var now = DateTimeOffset.UtcNow;
        _pipeline.Post(new LiveRegionChangedEvent(now, "3 new messages", LiveRegionPoliteness.Assertive, "1,2"));
        _pipeline.Post(new LiveRegionChangedEvent(now, "3 new messages", LiveRegionPoliteness.Assertive, "1,2"));

        await Task.Delay(300);

        lock (_spokenUtterances)
            Assert.Single(_spokenUtterances, u => u.Text == "3 new messages");
    }

    [Fact]
    public async Task ModeChanged_PlaysCueOnce()
    {
        _pipeline.Post(new ModeChangedEvent(DateTimeOffset.UtcNow, InteractionMode.Focus));

        await Task.Delay(200);

        lock (_playedCues)
            Assert.Single(_playedCues, c => c == "focus_mode");
    }

    [Fact]
    public async Task BrowseEvents_AreRaisedToSubscribers()
    {
        var raised = new List<string>();
        _pipeline.DocumentChangedProcessed += (_, _) => { lock (raised) raised.Add("document"); };
        _pipeline.SubtreeChangedProcessed += (_, _) => { lock (raised) raised.Add("subtree"); };
        _pipeline.StructureChangedProcessed += (_, _) => { lock (raised) raised.Add("structure"); };
        _pipeline.ElementsListClosedProcessed += (_, _) => { lock (raised) raised.Add("elements"); };

        var now = DateTimeOffset.UtcNow;
        _pipeline.Post(new DocumentChangedEvent(now, null));
        _pipeline.Post(new SubtreeChangedEvent(now, [1], null));
        _pipeline.Post(new StructureChangedEvent(now, [1]));
        _pipeline.Post(new ElementsListClosedEvent(now, null));

        await Task.Delay(200);

        lock (raised)
            Assert.Equal(["document", "subtree", "structure", "elements"], raised);
    }

    [Fact]
    public async Task LiveRegion_PoliteUpdateDuringCooldown_IsAnnouncedAfterIt()
    {
        var now = DateTimeOffset.UtcNow;
        _pipeline.Post(new LiveRegionChangedEvent(now, "Loading", LiveRegionPoliteness.Polite, "5,6"));
        _pipeline.Post(new LiveRegionChangedEvent(now, "Done", LiveRegionPoliteness.Polite, "5,6"));

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            lock (_spokenUtterances)
                if (_spokenUtterances.Any(u => u.Text == "Done")) break;
            await Task.Delay(20);
        }

        lock (_spokenUtterances)
        {
            Assert.Contains(_spokenUtterances, u => u.Text == "Loading");
            Assert.Contains(_spokenUtterances, u => u.Text == "Done");
        }
    }

    [Fact]
    public async Task Focus_WithNothingToSay_DoesNotInterruptSpeech()
    {
        _pipeline.Post(new FocusChangedEvent(DateTimeOffset.UtcNow, "", "Group"));

        await Task.Delay(200);

        _engineMock.Verify(e => e.Cancel(), Times.Never);
        lock (_spokenUtterances) Assert.Empty(_spokenUtterances);
    }

    [Fact]
    public async Task Focus_RejectedByFilter_IsNotAnnounced()
    {
        _pipeline.FocusAnnouncementFilter = f => f.ElementName != "Old place";
        _pipeline.Post(new FocusChangedEvent(DateTimeOffset.UtcNow, "Old place", "Hyperlink"));

        await Task.Delay(200);

        lock (_spokenUtterances) Assert.DoesNotContain(_spokenUtterances, u => u.Text.Contains("Old place"));
    }

    [Fact]
    public async Task Notifications_FromForegroundAreSpoken_BackgroundIgnored()
    {
        var now = DateTimeOffset.UtcNow;
        _pipeline.Post(new NotificationEvent(now, "a", "Download complete", Processing: 0));
        _pipeline.Post(new NotificationEvent(now, "b", "Background app", Processing: 2, IsFromForeground: false)); // not important

        await Task.Delay(300);

        lock (_spokenUtterances)
        {
            var spoken = Assert.Single(_spokenUtterances);
            Assert.Equal("Download complete", spoken.Text);
            Assert.Equal(SpeechPriority.High, spoken.Priority);
        }
    }

    [Fact]
    public async Task Notifications_MostRecentKind_SpeaksOnlyTheLatest()
    {
        var now = DateTimeOffset.UtcNow;
        _pipeline.Post(new NotificationEvent(now, "progress", "10 percent", Processing: 3));
        _pipeline.Post(new NotificationEvent(now, "progress", "20 percent", Processing: 3));
        _pipeline.Post(new NotificationEvent(now, "progress", "30 percent", Processing: 3));

        await Task.Delay(500);

        lock (_spokenUtterances)
            Assert.Equal(["30 percent"], _spokenUtterances.Select(u => u.Text));
    }

    [Fact]
    public async Task LiveRegion_SameTextAfterBeingCleared_IsSpokenAgain()
    {
        var now = DateTimeOffset.UtcNow;
        _pipeline.Post(new LiveRegionChangedEvent(now, "Item added", LiveRegionPoliteness.Assertive, "9,9"));
        _pipeline.Post(new LiveRegionChangedEvent(now, "", LiveRegionPoliteness.Assertive, "9,9"));
        _pipeline.Post(new LiveRegionChangedEvent(now, "Item added", LiveRegionPoliteness.Assertive, "9,9"));

        await Task.Delay(300);

        lock (_spokenUtterances)
            Assert.Equal(2, _spokenUtterances.Count(u => u.Text == "Item added"));
    }
}
