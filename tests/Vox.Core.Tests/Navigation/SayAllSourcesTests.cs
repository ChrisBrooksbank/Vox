using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Navigation;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Vox.Core.Text;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class SayAllSourcesTests
{
    private static TextDocumentSayAllSource Source(ITextDocument document) =>
        new(() => document, read => Task.FromResult(read()));

    [Fact]
    public async Task TextDocumentSource_ReadsFromTheCaretToTheEnd()
    {
        var source = Source(new StringTextDocument("first line\nsecond\nthird", caret: 6));

        Assert.Equal("line\n", await source.CurrentLineAsync(default));
        Assert.Equal("second\n", await source.NextLineAsync(default));
        Assert.Equal("third", await source.NextLineAsync(default));
        Assert.Null(await source.NextLineAsync(default));
    }

    [Fact]
    public async Task TextDocumentSource_NoDocument_ReadsNothing()
    {
        var source = new TextDocumentSayAllSource(() => null, read => Task.FromResult(read()));

        Assert.Null(await source.CurrentLineAsync(default));
        Assert.Null(await source.NextLineAsync(default));
    }

    [Fact]
    public async Task SayAllController_OverATextDocument_SpeaksEachLine()
    {
        var engine = new RecordingSpeechEngine();
        using var queue = new SpeechQueue(engine, NullLogger<SpeechQueue>.Instance);
        var sayAll = new SayAllController(queue, NullLogger<SayAllController>.Instance);

        sayAll.Start(Source(new StringTextDocument("alpha\n\nbeta", caret: 0)));

        await engine.WaitForTextAsync("beta");
        Assert.Equal(["alpha", "beta"], engine.SpokenText);
    }
}
