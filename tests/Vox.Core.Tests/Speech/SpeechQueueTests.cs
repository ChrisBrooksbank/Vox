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

        _engineMock
            .Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Callback<Utterance, CancellationToken>((u, _) => speakOrder.Add(u.Text))
            .Returns(Task.CompletedTask);

        _queue.Enqueue(new Utterance("Low", SpeechPriority.Low));
        _queue.Enqueue(new Utterance("High", SpeechPriority.High));

        await Task.Delay(500);

        // High priority should be spoken before Low when both are queued together
        Assert.Contains("High", speakOrder);
        Assert.Contains("Low", speakOrder);

        var highIndex = speakOrder.IndexOf("High");
        var lowIndex = speakOrder.IndexOf("Low");
        Assert.True(highIndex < lowIndex, $"High ({highIndex}) should come before Low ({lowIndex})");
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var queue = new SpeechQueue(_engineMock.Object, NullLogger<SpeechQueue>.Instance);
        var ex = Record.Exception(() => queue.Dispose());
        Assert.Null(ex);
    }
}
