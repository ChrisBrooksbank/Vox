using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Speech;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class SpeechQueueTests
{
    private readonly Mock<ISpeechEngine> _engineMock;
    private readonly SpeechQueue _queue;

    public SpeechQueueTests()
    {
        _engineMock = new Mock<ISpeechEngine>();
        _engineMock
            .Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _queue = new SpeechQueue(_engineMock.Object, NullLogger<SpeechQueue>.Instance);
    }

    [Fact]
    public async Task Enqueue_SingleUtterance_SpeaksIt()
    {
        var utterance = new Utterance("Hello", SpeechPriority.Normal);
        _queue.Enqueue(utterance);

        await Task.Delay(500); // Give queue time to process

        _engineMock.Verify(e => e.SpeakAsync(
            It.Is<Utterance>(u => u.Text == "Hello"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Enqueue_InterruptPriority_CancelsCurrentSpeech()
    {
        var normal = new Utterance("Normal speech", SpeechPriority.Normal);
        var interrupt = new Utterance("Interrupt!", SpeechPriority.Interrupt);

        _queue.Enqueue(normal);
        _queue.Enqueue(interrupt);

        await Task.Delay(500);

        // Cancel should be called for interrupt priority
        _engineMock.Verify(e => e.Cancel(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Enqueue_MultipleNormalUtterances_CoalescesWithinWindow()
    {
        // Enqueue multiple Normal utterances rapidly
        _queue.Enqueue(new Utterance("First", SpeechPriority.Normal));
        _queue.Enqueue(new Utterance("Second", SpeechPriority.Normal));
        _queue.Enqueue(new Utterance("Third", SpeechPriority.Normal));

        await Task.Delay(500);

        // Should be coalesced into fewer speak calls
        var calls = _engineMock.Invocations
            .Where(i => i.Method.Name == nameof(ISpeechEngine.SpeakAsync))
            .ToList();

        // All three were coalesced, so there should be fewer calls than utterances
        // (at most 1-2 calls, not 3)
        Assert.True(calls.Count <= 2, $"Expected coalescing to reduce calls, but got {calls.Count} calls");
    }

    [Fact]
    public async Task Enqueue_HighPriorityBeforeLow_HighSpeaksFirst()
    {
        var speakOrder = new List<string>();
        var busyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBusy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _engineMock
            .Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Returns<Utterance, CancellationToken>((u, _) =>
            {
                lock (speakOrder) speakOrder.Add(u.Text);
                if (u.Text != "Busy")
                    return Task.CompletedTask;
                busyStarted.TrySetResult();
                return releaseBusy.Task;
            });

        // Keep the engine busy so both are queued before either is taken (otherwise the reader
        // can pick up Low before High has been enqueued)
        _queue.Enqueue(new Utterance("Busy", SpeechPriority.Normal));
        await busyStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        _queue.Enqueue(new Utterance("Low", SpeechPriority.Low));
        _queue.Enqueue(new Utterance("High", SpeechPriority.High));
        releaseBusy.SetResult();

        await Task.Delay(500);

        // High priority should be spoken before Low when both are queued together
        Assert.Contains("High", speakOrder);
        Assert.Contains("Low", speakOrder);

        var highIndex = speakOrder.IndexOf("High");
        var lowIndex = speakOrder.IndexOf("Low");
        Assert.True(highIndex < lowIndex, $"High ({highIndex}) should come before Low ({lowIndex})");
    }

    /// <summary>
    /// Engine whose SpeakAsync blocks until its token is cancelled (or it is released).
    /// </summary>
    private sealed class BlockingEngine : ISpeechEngine
    {
        public List<string> Started { get; } = new();
        public List<string> Cancelled { get; } = new();
        public bool IsSpeaking => false;

        public async Task SpeakAsync(Utterance utterance, CancellationToken cancellationToken = default)
        {
            lock (Started) Started.Add(utterance.Text);
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                lock (Cancelled) Cancelled.Add(utterance.Text);
                throw;
            }
        }

        public void Cancel() { }
        public void SetRate(int wpm) { }
        public void SetVoice(string voiceName) { }
        public IReadOnlyList<string> GetAvailableVoices() => [];
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    [Fact]
    public async Task Interrupt_WhileLongUtteranceSpeaking_CancelsItImmediately()
    {
        var engine = new BlockingEngine();
        using var queue = new SpeechQueue(engine, NullLogger<SpeechQueue>.Instance);

        queue.Enqueue(new Utterance("Long text", SpeechPriority.High));
        await WaitUntil(() => { lock (engine.Started) return engine.Started.Contains("Long text"); });

        queue.Enqueue(new Utterance("Focus moved", SpeechPriority.Interrupt));
        await WaitUntil(() => { lock (engine.Started) return engine.Started.Contains("Focus moved"); });

        lock (engine.Cancelled) Assert.Contains("Long text", engine.Cancelled);
        lock (engine.Started) Assert.Contains("Focus moved", engine.Started);
    }

    [Fact]
    public async Task Interrupt_DropsUtterancesQueuedBeforeIt()
    {
        var engine = new BlockingEngine();
        using var queue = new SpeechQueue(engine, NullLogger<SpeechQueue>.Instance);

        queue.Enqueue(new Utterance("Speaking", SpeechPriority.High));
        await WaitUntil(() => { lock (engine.Started) return engine.Started.Count == 1; });

        queue.Enqueue(new Utterance("Stale", SpeechPriority.Low));
        queue.Enqueue(new Utterance("Now", SpeechPriority.Interrupt));
        await WaitUntil(() => { lock (engine.Started) return engine.Started.Contains("Now"); });
        await Task.Delay(100);

        lock (engine.Started) Assert.DoesNotContain("Stale", engine.Started);
    }

    [Fact]
    public async Task CancelAll_StopsCurrentAndFlushesPending()
    {
        var engine = new BlockingEngine();
        using var queue = new SpeechQueue(engine, NullLogger<SpeechQueue>.Instance);

        queue.Enqueue(new Utterance("One", SpeechPriority.High));
        await WaitUntil(() => { lock (engine.Started) return engine.Started.Count == 1; });
        var pending = queue.EnqueueAndWaitAsync(new Utterance("Two", SpeechPriority.High));

        queue.CancelAll();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await Task.Delay(100);
        lock (engine.Started) Assert.DoesNotContain("Two", engine.Started);
        lock (engine.Cancelled) Assert.Contains("One", engine.Cancelled);
    }

    [Fact]
    public async Task EnqueueAndWaitAsync_CompletesAfterSpeaking()
    {
        var spoken = new TaskCompletionSource();
        _engineMock
            .Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Returns(() => spoken.Task);

        var task = _queue.EnqueueAndWaitAsync(new Utterance("Line", SpeechPriority.Normal));
        await Task.Delay(150);
        Assert.False(task.IsCompleted);

        spoken.SetResult();
        await task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var queue = new SpeechQueue(_engineMock.Object, NullLogger<SpeechQueue>.Instance);
        var ex = Record.Exception(() => queue.Dispose());
        Assert.Null(ex);
    }
}

public class SpeechQueueReorderingTests
{
    /// <summary>Engine whose utterances finish only when the test releases them.</summary>
    private sealed class GatedEngine : ISpeechEngine
    {
        private readonly object _lock = new();
        private TaskCompletionSource? _current;
        public List<string> Started { get; } = new();
        public bool IsSpeaking => false;

        public async Task SpeakAsync(Utterance utterance, CancellationToken cancellationToken = default)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock) { Started.Add(utterance.Text); _current = gate; }
            using (cancellationToken.Register(() => gate.TrySetCanceled()))
                await gate.Task;
        }

        public void Release() { lock (_lock) _current?.TrySetResult(); }
        public int StartedCount { get { lock (_lock) return Started.Count; } }
        public List<string> Snapshot() { lock (_lock) return Started.ToList(); }

        public void Cancel() { }
        public void SetRate(int wpm) { }
        public void SetVoice(string voiceName) { }
        public IReadOnlyList<string> GetAvailableVoices() => [];
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    [Fact]
    public async Task HighUtterance_OvertakesLowOnesQueuedBeforeIt()
    {
        var engine = new GatedEngine();
        using var queue = new SpeechQueue(engine, NullLogger<SpeechQueue>.Instance);

        queue.Enqueue(new Utterance("first", SpeechPriority.Low));
        await WaitUntil(() => engine.StartedCount == 1);

        // Two polite updates queue up while "first" is being spoken
        queue.Enqueue(new Utterance("low a", SpeechPriority.Low));
        queue.Enqueue(new Utterance("low b", SpeechPriority.Low));
        engine.Release(); // "first" done; "low a" starts
        await WaitUntil(() => engine.StartedCount == 2);

        queue.Enqueue(new Utterance("urgent", SpeechPriority.High));
        engine.Release(); // "low a" done
        await WaitUntil(() => engine.StartedCount == 3);
        engine.Release();
        await WaitUntil(() => engine.StartedCount == 4);
        engine.Release();

        Assert.Equal(["first", "low a", "urgent", "low b"], engine.Snapshot());
    }
}

public class SpeechQueueSuspendTests
{
    [Fact]
    public async Task WhileSuspended_NothingIsSpokenOrCancelled()
    {
        var engine = new Mock<ISpeechEngine>();
        var spoken = new List<string>();
        engine.Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Callback<Utterance, CancellationToken>((u, _) => { lock (spoken) spoken.Add(u.Text); })
            .Returns(Task.CompletedTask);
        using var queue = new SpeechQueue(engine.Object, NullLogger<SpeechQueue>.Instance);

        queue.Suspend();
        engine.Invocations.Clear();
        queue.Enqueue(new Utterance("focus announcement", SpeechPriority.Interrupt));
        await queue.EnqueueAsync(new Utterance("live region", SpeechPriority.Low));
        var waited = queue.EnqueueAndWaitAsync(new Utterance("say all", SpeechPriority.Normal));
        await Task.Delay(150);

        lock (spoken) Assert.Empty(spoken);
        engine.Verify(e => e.Cancel(), Times.Never); // the wizard's own speech is left alone
        Assert.True(waited.IsCanceled);

        queue.Resume();
        queue.Enqueue(new Utterance("back", SpeechPriority.Normal));
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline) { lock (spoken) if (spoken.Contains("back")) break; await Task.Delay(10); }
        lock (spoken) Assert.Contains("back", spoken);
    }
}
