using Moq;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Logging;

/// <summary>
/// A screen reader sees every key the user types. Logs are files on disk that people attach to bug
/// reports, so typed characters, words, password field input and form field values must never
/// reach a logger, at any level.
/// </summary>
public class SensitiveTextLoggingTests
{
    private const string Secret = "hunter2zqx";

    private sealed class NullSink : IEventSink
    {
        public void Post(ScreenReaderEvent evt) { }
    }

    private static RawKeyEvent KeyUp(int vkCode) =>
        new(DateTimeOffset.UtcNow, new KeyEvent { VkCode = vkCode, IsKeyDown = false, Timestamp = 0 });

    private static void Type(TypingEchoHandler handler, string lowercaseText)
    {
        foreach (var ch in lowercaseText)
            handler.HandleKeyEvent(KeyUp(char.ToUpperInvariant(ch)));  // VK codes of letters and digits are their uppercase ASCII
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypingEcho_NeverLogsTypedCharactersOrWords(bool passwordMode)
    {
        var logger = new CapturingLogger<TypingEchoHandler>();
        var handler = new TypingEchoHandler(new NullSink(), () => TypingEchoMode.Both, logger)
        {
            PasswordMode = passwordMode,
        };

        Type(handler, Secret);
        handler.HandleKeyEvent(KeyUp(0x20)); // Space ends the word

        var logged = logger.AllText;
        Assert.DoesNotContain(Secret, logged, StringComparison.OrdinalIgnoreCase);
        foreach (var entry in logger.Entries)
            Assert.DoesNotContain(entry.Values, v => v.Length == 1 && Secret.Contains(v, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ThrowingEngine : ISpeechEngine
    {
        public bool IsSpeaking => false;
        public Task SpeakAsync(Utterance utterance, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("engine failure");
        public void Cancel() { }
        public void SetRate(int wpm) { }
        public void SetVoice(string voiceName) { }
        public IReadOnlyList<string> GetAvailableVoices() => [];
    }

    private sealed class BlockingEngine : ISpeechEngine
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsSpeaking => false;
        public async Task SpeakAsync(Utterance utterance, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        public void Cancel() { }
        public void SetRate(int wpm) { }
        public void SetVoice(string voiceName) { }
        public IReadOnlyList<string> GetAvailableVoices() => [];
    }

    [Fact]
    public async Task SpeechQueue_EngineError_DoesNotLogUtteranceText()
    {
        var logger = new CapturingLogger<SpeechQueue>();
        using var queue = new SpeechQueue(new ThrowingEngine(), logger);

        await queue.EnqueueAndWaitAsync(new Utterance(Secret, SpeechPriority.Normal))
            .WaitAsync(TimeSpan.FromSeconds(2))
            .ContinueWith(_ => { });
        await WaitUntil(() => logger.Entries.Any(e => e.ExceptionMessage == "engine failure"));

        Assert.DoesNotContain(Secret, logger.AllText);
    }

    [Fact]
    public async Task SpeechQueue_InterruptedUtterance_DoesNotLogItsText()
    {
        var logger = new CapturingLogger<SpeechQueue>();
        var engine = new BlockingEngine();
        using var queue = new SpeechQueue(engine, logger);

        queue.Enqueue(new Utterance(Secret, SpeechPriority.Normal));
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        queue.Enqueue(new Utterance("next", SpeechPriority.Interrupt));
        await WaitUntil(() => logger.Entries.Any(e => e.Message.Contains("interrupted")));

        Assert.DoesNotContain(Secret, logger.AllText);
    }

    [Fact]
    public async Task EventPipeline_PropertyChange_DoesNotLogTheNewValue()
    {
        var logger = new CapturingLogger<EventPipeline>();
        var engine = new RecordingSpeechEngine();
        using var queue = new SpeechQueue(engine, new CapturingLogger<SpeechQueue>());
        using var pipeline = new EventPipeline(queue, Mock.Of<IAudioCuePlayer>(), logger);
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.PropertyChangedProcessed += (_, _) => processed.TrySetResult();

        pipeline.Post(new PropertyChangedEvent(DateTimeOffset.UtcNow, [42, 1], 30045 /* Value.Value */, Secret));
        await processed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(Secret, logger.AllText);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(condition(), "condition not met in time");
    }
}
