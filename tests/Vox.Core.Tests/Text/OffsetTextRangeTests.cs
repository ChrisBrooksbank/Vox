using Vox.Core.Text;
using Xunit;

namespace Vox.Core.Tests.Text;

public class OffsetTextRangeTests
{
    private static OffsetTextRange At(string text, int offset) => new(text, offset, offset);

    [Theory]
    [InlineData("hello world", 0, "h")]
    [InlineData("hello world", 5, " ")]
    [InlineData("a\r\nb", 1, "\r\n")]
    [InlineData("a\r\nb", 2, "\r\n")]   // inside the pair
    [InlineData("x😀y", 1, "😀")]
    [InlineData("x😀y", 2, "😀")]
    public void ExpandToCharacter(string text, int offset, string expected)
    {
        Assert.Equal(expected, At(text, offset).ExpandToEnclosingUnit(TextUnit.Character).GetText());
    }

    [Theory]
    [InlineData("hello big world", 0, "hello ")]
    [InlineData("hello big world", 3, "hello ")]
    [InlineData("hello big world", 5, "hello ")]  // trailing space belongs to the word before it
    [InlineData("hello big world", 7, "big ")]
    [InlineData("hello big world", 13, "world")]
    [InlineData("  indented text", 1, "  ")]       // leading spaces are a word of their own
    [InlineData("one\ntwo three", 4, "two ")]      // words don't cross lines
    [InlineData("one two\nthree", 4, "two")]
    public void ExpandToWord(string text, int offset, string expected)
    {
        Assert.Equal(expected, At(text, offset).ExpandToEnclosingUnit(TextUnit.Word).GetText());
    }

    [Theory]
    [InlineData("first\nsecond\nthird", 0, "first\n")]
    [InlineData("first\nsecond\nthird", 5, "first\n")]  // on the line break
    [InlineData("first\nsecond\nthird", 6, "second\n")]
    [InlineData("first\r\nsecond", 6, "first\r\n")]     // on the \n of \r\n
    [InlineData("first\nsecond\nthird", 17, "third")]
    [InlineData("first\nsecond\nthird", 18, "third")]   // the end of the text is on the last line
    public void ExpandToLine(string text, int offset, string expected)
    {
        Assert.Equal(expected, At(text, offset).ExpandToEnclosingUnit(TextUnit.Line).GetText());
    }

    [Fact]
    public void Move_DegenerateByWord_LandsOnWordStarts()
    {
        const string text = "one two three";
        var (range, moved) = At(text, 0).Move(TextUnit.Word, 2);

        Assert.Equal(2, moved);
        Assert.True(range.IsDegenerate);
        Assert.Equal("three", range.ExpandToEnclosingUnit(TextUnit.Word).GetText());
    }

    [Fact]
    public void Move_BackwardFromInsideAWord_FirstReachesItsStart()
    {
        const string text = "one two three";
        var (range, moved) = At(text, 9).Move(TextUnit.Word, -1); // inside "three"

        Assert.Equal(-1, moved);
        Assert.Equal(8, ((OffsetTextRange)range).Start);
    }

    [Fact]
    public void Move_PastTheEnd_StopsAndReportsFewerUnits()
    {
        const string text = "a\nb\nc";
        var (range, moved) = At(text, 0).Move(TextUnit.Line, 5);

        Assert.Equal(2, moved);
        Assert.Equal("c", range.ExpandToEnclosingUnit(TextUnit.Line).GetText());
    }

    [Fact]
    public void Move_BeforeTheStart_StopsAtZero()
    {
        var (range, moved) = At("a\nb", 2).Move(TextUnit.Line, -3);

        Assert.Equal(-1, moved);
        Assert.Equal(0, ((OffsetTextRange)range).Start);
    }

    [Fact]
    public void Move_NonDegenerate_BecomesTheWholeUnit()
    {
        var line = new OffsetTextRange("a\nbb\nccc", 0, 2);
        var (range, _) = line.Move(TextUnit.Line, 1);

        Assert.Equal("bb\n", range.GetText());
    }

    [Fact]
    public void CompareAndWithEndpoint()
    {
        const string text = "0123456789";
        var a = new OffsetTextRange(text, 2, 5);
        var b = new OffsetTextRange(text, 4, 8);

        Assert.True(a.CompareEndpoints(TextEndpoint.Start, b, TextEndpoint.Start) < 0);
        Assert.True(a.CompareEndpoints(TextEndpoint.End, b, TextEndpoint.Start) > 0);
        Assert.Equal(0, a.CompareEndpoints(TextEndpoint.Start, a, TextEndpoint.Start));
        Assert.Equal("234567", a.WithEndpoint(TextEndpoint.End, b, TextEndpoint.End).GetText());
        Assert.Equal("567", b.WithEndpoint(TextEndpoint.Start, a, TextEndpoint.End).GetText());
    }

    [Fact]
    public void GetText_MaxLength()
    {
        Assert.Equal("hel", new OffsetTextRange("hello", 0, 5).GetText(3));
    }

    [Fact]
    public void StringTextDocument_CaretAndSelection()
    {
        var doc = new StringTextDocument("hello world", caret: 6, selectionStart: 6, selectionEnd: 11);

        Assert.True(doc.GetCaret()!.IsDegenerate);
        Assert.Equal("world", Assert.Single(doc.GetSelection()).GetText());
        Assert.Equal("hello world", doc.DocumentRange.GetText());
    }

    [Fact]
    public void Attributes_ComeFromTheDocument()
    {
        var doc = new StringTextDocument("ab", attributesAt: i => new TextAttributes { IsBold = i == 1 });

        Assert.True(doc.Range(1, 2).GetAttributes().IsBold);
        Assert.False(doc.Range(0, 1).GetAttributes().IsBold);
    }
}
