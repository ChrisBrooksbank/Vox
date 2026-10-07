using Moq;
using Vox.Core.Audio;
using Vox.Core.Buffer;
using Vox.Core.Tests.Buffer;
using Vox.Core.Text;
using Xunit;

namespace Vox.Core.Tests.Text;

public class BufferTextDocumentTests
{
    private static VBufferCursor Cursor(params string[] paragraphs)
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        int id = 2;
        foreach (var text in paragraphs)
            root.AddChild(new MockElement { RuntimeId = [id++], Name = text, ControlType = "Group" }
                .AddChild(new MockElement { RuntimeId = [id++], Name = text }));
        return new VBufferCursor(new VBufferBuilder().Build(root), Mock.Of<IAudioCuePlayer>());
    }

    [Fact]
    public void Caret_IsTheCursorOffset()
    {
        var cursor = Cursor("First paragraph", "Second paragraph");
        cursor.NextLine();
        var doc = new BufferTextDocument(cursor);

        var caret = (OffsetTextRange)doc.GetCaret()!;

        Assert.Equal(cursor.TextOffset, caret.Start);
        Assert.True(caret.IsDegenerate);
        Assert.Equal(caret.Start, ((OffsetTextRange)Assert.Single(doc.GetSelection())).Start);
    }

    [Fact]
    public void Lines_AreTheCursorsWrappedLines()
    {
        var longText = string.Join(" ", Enumerable.Repeat("word", 40)); // 199 characters
        var cursor = Cursor(longText);
        cursor.MaxLineLength = 50;
        var doc = new BufferTextDocument(cursor);

        var firstLine = doc.GetCaret()!.ExpandToEnclosingUnit(TextUnit.Line).GetText().TrimEnd();
        var (second, moved) = doc.GetCaret()!.Move(TextUnit.Line, 1);

        Assert.Equal(1, moved);
        Assert.Equal(cursor.ReadLineAt(0), firstLine);
        Assert.Equal(cursor.ReadLineAt(cursor.LineEndAt(0)),
            second.ExpandToEnclosingUnit(TextUnit.Line).GetText().TrimEnd());
    }

    [Fact]
    public void Paragraphs_AreWholeBlocks()
    {
        var longText = string.Join(" ", Enumerable.Repeat("word", 40));
        var cursor = Cursor(longText, "Next");
        cursor.MaxLineLength = 50;
        var doc = new BufferTextDocument(cursor);

        Assert.Equal(longText + "\n", doc.GetCaret()!.ExpandToEnclosingUnit(TextUnit.Paragraph).GetText());
    }

    [Fact]
    public void DocumentRange_IsTheWholeBuffer()
    {
        var cursor = Cursor("One", "Two");

        Assert.Equal(cursor.Document.FlatText, new BufferTextDocument(cursor).DocumentRange.GetText());
    }
}
