using Vox.Core.Text;
using Xunit;

namespace Vox.Core.Tests.Text;

public class TextLinesTests
{
    // Lines: "first\n" [0,6), "second line\n" [6,18), "third" [18,23)
    private const string Text = "first\nsecond line\nthird";

    private static readonly StringTextDocument Document = new(Text);

    private static OffsetTextRange At(int offset) => Document.Range(offset, offset);

    private static OffsetTextRange Range(ITextRange range) => Assert.IsType<OffsetTextRange>(range);

    [Fact]
    public void LineAt_GivesTheLineAndTheOffsetInIt()
    {
        var line = TextLines.LineAt(At(9));

        Assert.Equal("second line\n", line.Text);
        Assert.Equal(3, line.Offset);
        Assert.Equal((6, 18), (Range(line.Line).Start, Range(line.Line).End));
    }

    [Theory]
    [InlineData(0, "first\n", 0)]
    [InlineData(5, "first\n", 5)]
    [InlineData(6, "second line\n", 0)]
    [InlineData(18, "third", 0)]
    [InlineData(23, "third", 5)]
    public void LineAt_LineStartsAndEnds(int offset, string text, int inLine)
    {
        var line = TextLines.LineAt(At(offset));

        Assert.Equal(text, line.Text);
        Assert.Equal(inLine, line.Offset);
    }

    [Fact]
    public void LineAt_UsesTheStartOfARange()
    {
        var line = TextLines.LineAt(Document.Range(8, 20));

        Assert.Equal("second line\n", line.Text);
        Assert.Equal(2, line.Offset);
    }

    [Fact]
    public void LineAt_LongLine_IsCut()
    {
        var text = new string('x', TextLines.MaxLineLength + 500);
        var line = TextLines.LineAt(new StringTextDocument(text).Range(TextLines.MaxLineLength + 100, TextLines.MaxLineLength + 100));

        Assert.Equal(TextLines.MaxLineLength, line.Text.Length);
        Assert.Equal(TextLines.MaxLineLength, line.Offset);
    }

    [Theory]
    [InlineData(8, 10, 2, 4)]     // inside the line
    [InlineData(2, 9, 0, 3)]      // from the line before
    [InlineData(10, 20, 4, 12)]   // into the line after
    [InlineData(0, 23, 0, 12)]    // the whole document
    [InlineData(6, 18, 0, 12)]    // exactly the line
    public void SelectionIn_GivesTheLinesPart(int start, int end, int expectedStart, int expectedEnd)
    {
        var line = TextLines.LineAt(At(9));

        Assert.Equal((expectedStart, expectedEnd), TextLines.SelectionIn(line, Document.Range(start, end)));
    }

    [Theory]
    [InlineData(0, 3)]    // before the line
    [InlineData(0, 6)]    // ends where the line starts
    [InlineData(18, 23)]  // starts where the line ends
    [InlineData(9, 9)]    // nothing selected
    public void SelectionIn_NothingOnTheLine_IsNull(int start, int end)
    {
        var line = TextLines.LineAt(At(9));

        Assert.Null(TextLines.SelectionIn(line, Document.Range(start, end)));
    }

    [Fact]
    public void SelectionIn_NoSelection_IsNull()
    {
        Assert.Null(TextLines.SelectionIn(TextLines.LineAt(At(9)), null));
    }

    [Theory]
    [InlineData(0, 6)]
    [InlineData(4, 10)]
    [InlineData(11, 17)]
    [InlineData(-3, 6)]
    public void PositionIn_IsADegenerateRangeInTheLine(int offset, int expected)
    {
        var line = Document.Range(6, 18);

        var position = Range(TextLines.PositionIn(line, offset));

        Assert.True(position.IsDegenerate);
        Assert.Equal(expected, position.Start);
    }

    [Fact]
    public void PositionIn_PastTheLine_IsClampedToItsEnd()
    {
        var position = Range(TextLines.PositionIn(Document.Range(6, 18), 100));

        Assert.True(position.IsDegenerate);
        Assert.Equal(18, position.Start);
    }

    [Theory]
    [InlineData(5, 23)]
    [InlineData(50, 23)]
    [InlineData(4, 22)]
    public void PositionIn_LastLine_ReachesTheDocumentEnd(int offset, int expected)
    {
        // Routing to the cell after the last line's end puts the caret after its last character
        var position = Range(TextLines.PositionIn(Document.Range(18, 23), offset));

        Assert.True(position.IsDegenerate);
        Assert.Equal(expected, position.Start);
    }

    [Fact]
    public void PositionIn_RoundTripsWithLineAt()
    {
        for (var offset = 0; offset <= 5; offset++)
        {
            var line = Document.Range(18, 23);
            Assert.Equal(offset, TextLines.LineAt(TextLines.PositionIn(line, offset)).Offset);
        }
    }
}
