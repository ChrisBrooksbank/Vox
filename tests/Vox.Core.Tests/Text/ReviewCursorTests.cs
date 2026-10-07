using Vox.Core.Text;
using Xunit;

namespace Vox.Core.Tests.Text;

public class ReviewCursorTests
{
    private const string Text = "First line here\nSecond line\nThird";

    private static ReviewCursor At(int offset, string text = Text)
    {
        var document = new StringTextDocument(text, caret: offset);
        var cursor = new ReviewCursor();
        cursor.Attach(document);
        return cursor;
    }

    [Fact]
    public void Attach_StartsAtTheCaret()
    {
        var cursor = At(Text.IndexOf("Second"));

        Assert.Equal("Second line", cursor.Read(TextUnit.Line)?.Text);
    }

    [Fact]
    public void Attach_WithoutCaret_StartsAtTheTop()
    {
        var cursor = new ReviewCursor();
        cursor.Attach(new NoCaretDocument(Text));

        Assert.Equal("First line here", cursor.Read(TextUnit.Line)?.Text);
    }

    [Fact]
    public void Lines_NextAndPrevious()
    {
        var cursor = At(0);

        Assert.Equal(new ReviewResult("Second line", false), cursor.Move(TextUnit.Line, 1));
        Assert.Equal(new ReviewResult("Third", false), cursor.Move(TextUnit.Line, 1));
        Assert.Equal(new ReviewResult("Second line", false), cursor.Move(TextUnit.Line, -1));
    }

    [Fact]
    public void Lines_AtTheEnds_ReportBoundaryAndRereadTheLine()
    {
        var cursor = At(0);

        Assert.Equal(new ReviewResult("First line here", true), cursor.Move(TextUnit.Line, -1));
        cursor.Bottom();
        Assert.Equal(new ReviewResult("Third", true), cursor.Move(TextUnit.Line, 1));
    }

    [Fact]
    public void Words_MoveWithinAndAcrossLines()
    {
        var cursor = At(Text.IndexOf("here"));

        Assert.Equal("here", cursor.Read(TextUnit.Word)?.Text);
        Assert.Equal("Second", cursor.Move(TextUnit.Word, 1)?.Text);
        Assert.Equal("here", cursor.Move(TextUnit.Word, -1)?.Text);
        Assert.Equal("line", cursor.Move(TextUnit.Word, -1)?.Text);
    }

    [Fact]
    public void Characters_AreSpokenByName()
    {
        var cursor = At(Text.IndexOf(" line"));

        Assert.Equal("space", cursor.Read(TextUnit.Character)?.Text.ToLowerInvariant());
        Assert.Equal("l", cursor.Move(TextUnit.Character, 1)?.Text);
        cursor.Move(TextUnit.Character, -1);
        Assert.Equal("t", cursor.Move(TextUnit.Character, -1)?.Text);
    }

    [Fact]
    public void Characters_StopAtTheEnd()
    {
        var cursor = At(Text.Length - 1);

        Assert.Equal(new ReviewResult("d", true), cursor.Move(TextUnit.Character, 1));
    }

    [Fact]
    public void TopAndBottom()
    {
        var cursor = At(Text.IndexOf("Second"));

        Assert.Equal("First line here", cursor.Top()?.Text);
        Assert.Equal("Third", cursor.Bottom()?.Text);
    }

    [Fact]
    public void Bottom_SkipsTheEmptyLineAfterAFinalLineBreak()
    {
        var cursor = At(0, "One\nTwo\n");

        Assert.Equal("Two", cursor.Bottom()?.Text);
    }

    [Fact]
    public void ReviewingDoesNotMoveTheCaret()
    {
        var document = new StringTextDocument(Text, caret: 3);
        var cursor = new ReviewCursor();
        cursor.Attach(document);

        cursor.Move(TextUnit.Line, 1);

        Assert.Equal(3, document.Caret);
    }

    [Fact]
    public void EmptyDocument_ReadsBlank()
    {
        var cursor = At(0, "");

        Assert.Equal("blank", cursor.Read(TextUnit.Line)?.Text);
        Assert.True(cursor.Move(TextUnit.Line, 1)?.AtBoundary);
    }

    [Fact]
    public void NoDocument_ReadsNothing()
    {
        var cursor = new ReviewCursor();

        Assert.Null(cursor.Read(TextUnit.Line));
        Assert.Null(cursor.Move(TextUnit.Word, 1));
        Assert.Null(cursor.Top());
    }

    [Fact]
    public void TextOf_GivesTheRawUnit()
    {
        var cursor = At(Text.IndexOf("Second"));

        Assert.Equal("Second ", cursor.TextOf(TextUnit.Word));
    }

    private sealed class NoCaretDocument(string text) : ITextDocument
    {
        private readonly StringTextDocument _inner = new(text);
        public ITextRange DocumentRange => _inner.DocumentRange;
        public ITextRange? GetCaret() => null;
        public IReadOnlyList<ITextRange> GetSelection() => [];
    }
}
