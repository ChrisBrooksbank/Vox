using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class TextProcessorTests
{
    private sealed class Rule(Func<string, string> apply) : ITextRule
    {
        public string Apply(string text, Utterance utterance) => apply(text);
    }

    private static Utterance Say(string text) => new(text, SpeechPriority.Normal);

    [Fact]
    public void NoRules_LeavesTheTextAsItIs()
    {
        Assert.Equal("Hello, world!", TextProcessor.None.Process(Say("Hello, world!")).Text);
    }

    [Fact]
    public void Rules_RunInOrder()
    {
        var processor = new TextProcessor([new Rule(t => t + " one"), new Rule(t => t + " two")]);

        Assert.Equal("start one two", processor.Process(Say("start")).Text);
    }

    [Fact]
    public void ARuleThatThrows_IsSkipped_AndLogsNoText()
    {
        var logger = new CapturingLogger<TextProcessor>();
        var processor = new TextProcessor(
            [new Rule(_ => throw new InvalidOperationException("boom")), new Rule(t => t.ToUpperInvariant())], logger);

        Assert.Equal("SECRET", processor.Process(Say("secret")).Text);
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("secret"));
    }

    [Fact]
    public async Task Queue_SpeaksTheProcessedText()
    {
        var engine = new RecordingSpeechEngine();
        using var queue = new SpeechQueue(engine, NullLogger<SpeechQueue>.Instance)
        {
            TextProcessor = new TextProcessor([new Rule(t => t.Replace("&", " and "))]),
        };
        string? started = null;
        queue.UtteranceStarted += (_, u) => started = u.Text;

        queue.Enqueue(new Utterance("Tom&Jerry", SpeechPriority.Interrupt));

        await engine.WaitForTextAsync("Tom and Jerry");
        Assert.Equal("Tom&Jerry", started); // history and events keep the original
    }
}
