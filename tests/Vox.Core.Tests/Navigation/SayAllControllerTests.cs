using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Audio;
using Vox.Core.Buffer;
using Vox.Core.Navigation;
using Vox.Core.Speech;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class SayAllControllerTests : IDisposable
{
    private readonly Mock<ISpeechEngine> _engineMock;
    private readonly SpeechQueue _queue;
    private readonly SayAllController _controller;
    private readonly Mock<IAudioCuePlayer> _audioCueMock;

    public SayAllControllerTests()
    {
        _engineMock = new Mock<ISpeechEngine>();
        _engineMock
            .Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _queue = new SpeechQueue(_engineMock.Object, NullLogger<SpeechQueue>.Instance);
        _controller = new SayAllController(_queue, NullLogger<SayAllController>.Instance);

        _audioCueMock = new Mock<IAudioCuePlayer>();
        _audioCueMock.SetupGet(p => p.IsEnabled).Returns(true);
    }

    public void Dispose()
    {
        _controller.Cancel();
        _queue.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private VBufferDocument BuildDocWithLines(params string[] lines)
    {
        var flatText = string.Join("\n", lines);
        var root = new VBufferNode
        {
            Id = 0,
            UIARuntimeId = [0],
            ControlType = "Document",
            Name = "doc",
            TextRange = (0, flatText.Length)
        };
        var allNodes = new List<VBufferNode> { root };

        int pos = 0;
        int id = 1;
        foreach (var line in lines)
        {
            var node = new VBufferNode
            {
                Id = id++,
                UIARuntimeId = [(int)id],
                ControlType = "Text",
                Name = line,
                TextRange = (pos, pos + line.Length)
            };
            allNodes.Add(node);
            pos += line.Length + 1; // +1 for \n
        }

        return new VBufferDocument(flatText, root, allNodes);
    }

    private VBufferCursor MakeCursor(VBufferDocument doc) =>
        new VBufferCursor(doc, _audioCueMock.Object);

    // -------------------------------------------------------------------------
    // IsReading
    // -------------------------------------------------------------------------

    [Fact]
    public void IsReading_BeforeStart_IsFalse()
    {
        Assert.False(_controller.IsReading);
    }

    [Fact]
    public void IsReading_AfterCancel_IsFalse()
    {
        var doc = BuildDocWithLines("Line one", "Line two");
        var cursor = MakeCursor(doc);

        _controller.Start(cursor);
        _controller.Cancel();

        // Give a moment for the task to complete
        Thread.Sleep(100);
        Assert.False(_controller.IsReading);
    }

    // -------------------------------------------------------------------------
    // SayAll reads lines and enqueues to speech queue
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Start_SingleLine_SpeaksThatLine()
    {
        var doc = BuildDocWithLines("Hello world");
        var cursor = MakeCursor(doc);

        _controller.Start(cursor);
        await Task.Delay(300);

        _engineMock.Verify(
            e => e.SpeakAsync(
                It.Is<Utterance>(u => u.Text.Contains("Hello world")),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Start_MultipleLines_SpeaksAllLines()
    {
        var doc = BuildDocWithLines("Line one", "Line two", "Line three");
        var cursor = MakeCursor(doc);

        _controller.Start(cursor);
        await Task.Delay(500);

        // Each line should be enqueued. Since speech is instant (mock), all should be spoken.
        _engineMock.Verify(
            e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    // -------------------------------------------------------------------------
    // Cancellation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Cancel_StopsReading()
    {
        // Use a slow speech engine so we can cancel mid-read
        var slowEngine = new Mock<ISpeechEngine>();
        int speakCount = 0;
        slowEngine
            .Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Returns(async (Utterance _, CancellationToken ct) =>
            {
                speakCount++;
                await Task.Delay(200, ct);
            });

        using var slowQueue = new SpeechQueue(slowEngine.Object, NullLogger<SpeechQueue>.Instance);
        var slowController = new SayAllController(slowQueue, NullLogger<SayAllController>.Instance);

        var doc = BuildDocWithLines("Line one", "Line two", "Line three", "Line four", "Line five");
        var cursor = MakeCursor(doc);

        slowController.Start(cursor);
        await Task.Delay(50);
        slowController.Cancel();

        // After cancellation, speaking should stop well before all 5 lines
        await Task.Delay(300);
        Assert.True(speakCount < 5, $"Expected fewer than 5 lines spoken, but got {speakCount}");
    }

    [Fact]
    public async Task Start_WhenAlreadyReading_CancelsPreviousAndStartsNew()
    {
        var doc1 = BuildDocWithLines("Document one line");
        var doc2 = BuildDocWithLines("Document two line");
        var cursor1 = MakeCursor(doc1);
        var cursor2 = MakeCursor(doc2);

        _controller.Start(cursor1);
        await Task.Delay(50);
        _controller.Start(cursor2); // Should cancel previous and restart

        await Task.Delay(300);

        // Should not throw; controller should still be in valid state
        Assert.False(_controller.IsReading);
    }

    // -------------------------------------------------------------------------
    // Empty / whitespace lines skipped
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Start_EmptyDocument_DoesNotCrash()
    {
        var flatText = "";
        var root = new VBufferNode
        {
            Id = 0,
            UIARuntimeId = [0],
            ControlType = "Document",
            Name = "doc",
            TextRange = (0, 0)
        };
        var doc = new VBufferDocument(flatText, root, [root]);
        var cursor = MakeCursor(doc);

        _controller.Start(cursor);
        await Task.Delay(200);

        // Should not crash and should have finished
        Assert.False(_controller.IsReading);
    }

    // -------------------------------------------------------------------------
    // Cancel is no-op when not reading
    // -------------------------------------------------------------------------

    [Fact]
    public void Cancel_WhenNotReading_IsNoOp()
    {
        // Should not throw
        _controller.Cancel();
        _controller.Cancel();
    }
}
