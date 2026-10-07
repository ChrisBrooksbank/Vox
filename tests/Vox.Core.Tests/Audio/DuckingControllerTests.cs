using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Audio;

public class DuckingControllerTests
{
    private sealed class FakeDucker : IAudioDucker
    {
        public List<bool> Calls { get; } = new();
        public void SetDucked(bool ducked) { lock (Calls) Calls.Add(ducked); }
    }

    private readonly FakeDucker _ducker = new();

    [Fact]
    public void Always_DucksAtOnce_OffReleases()
    {
        using var controller = new DuckingController(_ducker);

        controller.SetMode(AudioDuckingMode.Always);
        Assert.True(controller.IsDucked);
        controller.SetMode(AudioDuckingMode.Off);
        Assert.False(controller.IsDucked);
        Assert.Equal([true, false], _ducker.Calls);
    }

    [Fact]
    public async Task WhileSpeaking_DucksDuringSpeech_AndReleasesAfterTheDelay()
    {
        using var controller = new DuckingController(_ducker, TimeSpan.FromMilliseconds(50));
        controller.SetMode(AudioDuckingMode.WhileSpeaking);
        Assert.False(controller.IsDucked);

        controller.OnSpeechStarted();
        Assert.True(controller.IsDucked);
        controller.OnSpeechEnded();
        Assert.True(controller.IsDucked); // not yet: the next utterance may follow at once

        await Task.Delay(200);
        Assert.False(controller.IsDucked);
    }

    [Fact]
    public async Task WhileSpeaking_UtterancesInQuickSuccession_StayDucked()
    {
        using var controller = new DuckingController(_ducker, TimeSpan.FromMilliseconds(100));
        controller.SetMode(AudioDuckingMode.WhileSpeaking);

        controller.OnSpeechStarted();
        controller.OnSpeechEnded();
        await Task.Delay(20);
        controller.OnSpeechStarted();
        await Task.Delay(150);

        Assert.True(controller.IsDucked);
        Assert.Equal([true], _ducker.Calls);
    }

    [Fact]
    public void Off_SpeechNeverDucks()
    {
        using var controller = new DuckingController(_ducker);

        controller.OnSpeechStarted();

        Assert.Empty(_ducker.Calls);
    }

    [Fact]
    public async Task SpeechQueue_RaisesStartAndFinishAroundEachUtterance()
    {
        var engine = new RecordingSpeechEngine();
        using var queue = new SpeechQueue(engine, NullLogger<SpeechQueue>.Instance);
        var events = new List<string>();
        queue.UtteranceStarted += (_, u) => { lock (events) events.Add("start " + u.Text); };
        queue.UtteranceFinished += (_, u) => { lock (events) events.Add("end " + u.Text); };

        await queue.EnqueueAndWaitAsync(new Utterance("hello", SpeechPriority.Interrupt));

        lock (events) Assert.Equal(["start hello", "end hello"], events);
    }
}
