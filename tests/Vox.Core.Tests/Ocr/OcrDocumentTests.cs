using Vox.Core.Ocr;
using Vox.Core.Text;
using Xunit;

namespace Vox.Core.Tests.Ocr;

public class OcrDocumentTests
{
    internal static OcrResult Result(params string[] lines)
    {
        // Words 10 pixels a character apart, lines 20 pixels apart
        var result = new List<OcrLine>();
        for (int l = 0; l < lines.Length; l++)
        {
            int x = 0;
            var words = new List<OcrWord>();
            foreach (var word in lines[l].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                words.Add(new OcrWord(word, new ScreenRect(x, l * 20, word.Length * 10, 16)));
                x += (word.Length + 1) * 10;
            }
            result.Add(new OcrLine(words));
        }
        return new OcrResult(result);
    }

    [Fact]
    public void Text_IsOneLinePerRecognisedLine()
    {
        var document = new OcrDocument(Result("File Edit View", "Save changes?"));

        Assert.Equal("File Edit View\nSave changes?", document.Text);
        Assert.Equal(2, document.LineCount);
        Assert.False(document.IsEmpty);
    }

    [Fact]
    public void EmptyResult_IsEmpty()
    {
        var document = new OcrDocument(new OcrResult([new OcrLine([])]));

        Assert.True(document.IsEmpty);
        Assert.Equal(0, document.LineCount);
        Assert.True(OcrResult.Empty.IsEmpty);
    }

    [Fact]
    public void ChineseAndJapanese_AreJoinedWithoutSpaces()
    {
        var line = new OcrLine([
            new OcrWord("文", new ScreenRect(0, 0, 10, 10)),
            new OcrWord("字", new ScreenRect(10, 0, 10, 10)),
            new OcrWord("OK", new ScreenRect(30, 0, 20, 10)),
        ]);

        Assert.Equal("文字 OK", new OcrDocument(new OcrResult([line])).Text);
    }

    [Fact]
    public void WordAt_FindsTheWordAndItsPlace()
    {
        var document = new OcrDocument(Result("File Edit View", "Save changes?"));

        Assert.Equal("Edit", document.WordAt(5)!.Text);
        Assert.Equal(new ScreenRect(50, 0, 40, 16), document.WordAt(7)!.Bounds);
        // On the space after a word: that word
        Assert.Equal("File", document.WordAt(4)!.Text);
        // On the line break: the line's last word, not the next line's first
        Assert.Equal("View", document.WordAt(14)!.Text);
        Assert.Equal("Save", document.WordAt(15)!.Text);
        Assert.Equal("changes?", document.WordAt(document.Text.Length)!.Text);
    }

    [Fact]
    public void Review_MovesThroughTheRecognisedText()
    {
        var document = new OcrDocument(Result("File Edit View", "Save changes?"));
        var cursor = new ReviewCursor();
        cursor.Attach(document);

        Assert.Equal("File Edit View", cursor.Read(TextUnit.Line)!.Value.Text);
        Assert.Equal("Save changes?", cursor.Move(TextUnit.Line, 1)!.Value.Text);
        Assert.Equal("changes?", cursor.Move(TextUnit.Word, 1)!.Value.Text.Trim());
        Assert.Equal("changes?", document.WordAt(cursor.Position!)!.Text);
    }

    [Fact]
    public void WordAt_ARangeFromAnotherDocument_IsNothing()
    {
        var document = new OcrDocument(Result("File"));

        Assert.Null(document.WordAt(new StringTextDocument("File").Range(0, 0)));
    }
}
