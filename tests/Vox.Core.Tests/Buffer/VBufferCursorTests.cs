using Moq;
using Vox.Core.Audio;
using Vox.Core.Buffer;
using Xunit;

namespace Vox.Core.Tests.Buffer;

public class VBufferCursorTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static VBufferDocument BuildDoc(string flatText)
    {
        var root = new VBufferNode
        {
            Id = 0,
            UIARuntimeId = [0],
            Name = "doc",
            ControlType = "Document",
            TextRange = (0, flatText.Length),
        };

        // Create one text node per line so FindNodeAtOffset works
        var allNodes = new List<VBufferNode> { root };
        int pos = 0;
        int id = 1;
        foreach (var line in flatText.Split('\n'))
        {
            if (line.Length == 0 && pos >= flatText.Length) break;
            int end = pos + line.Length + (pos + line.Length < flatText.Length ? 1 : 0); // include \n
            if (end > flatText.Length) end = flatText.Length;
            var node = new VBufferNode
            {
                Id = id++,
                UIARuntimeId = [id],
                Name = line,
                ControlType = "Text",
                TextRange = (pos, end),
            };
            node.Parent = root;
            root.Children.Add(node);
            allNodes.Add(node);
            pos = end;
            if (pos >= flatText.Length) break;
        }

        return new VBufferDocument(flatText, root, allNodes);
    }

    private static (VBufferCursor cursor, Mock<IAudioCuePlayer> mockPlayer)
        MakeCursor(string flatText, bool wrap = false)
    {
        var doc = BuildDoc(flatText);
        var mock = new Mock<IAudioCuePlayer>();
        mock.SetupGet(p => p.IsEnabled).Returns(true);
        var cursor = new VBufferCursor(doc, mock.Object) { WrapEnabled = wrap };
        return (cursor, mock);
    }

    // -------------------------------------------------------------------------
    // Character movement — forward
    // -------------------------------------------------------------------------

    [Fact]
    public void NextChar_AdvancesOffsetByOne()
    {
        var (cursor, _) = MakeCursor("abc");
        var ch = cursor.NextChar();
        Assert.Equal('b', ch);
        Assert.Equal(1, cursor.TextOffset);
    }

    [Fact]
    public void NextChar_AtEndPlays_Boundary_AndReturnsNull()
    {
        var (cursor, mock) = MakeCursor("a");
        cursor.NextChar(); // moves to offset 1 which equals Length — boundary
        mock.Verify(p => p.Play("boundary"), Times.Once);
        Assert.Null(cursor.NextChar()); // still at end
    }

    [Fact]
    public void NextChar_AtEnd_WithWrap_PlaysWrap_AndWrapsToStart()
    {
        var (cursor, mock) = MakeCursor("abc", wrap: true);
        // Advance to last char
        cursor.NextChar(); // 'b'
        cursor.NextChar(); // 'c'
        var ch = cursor.NextChar(); // should wrap
        mock.Verify(p => p.Play("wrap"), Times.Once);
        Assert.Equal('a', ch);
        Assert.Equal(0, cursor.TextOffset);
    }

    // -------------------------------------------------------------------------
    // Character movement — backward
    // -------------------------------------------------------------------------

    [Fact]
    public void PrevChar_DecrementsOffsetByOne()
    {
        var (cursor, _) = MakeCursor("abc");
        cursor.NextChar(); cursor.NextChar(); // offset = 2
        var ch = cursor.PrevChar();
        Assert.Equal('b', ch);
        Assert.Equal(1, cursor.TextOffset);
    }

    [Fact]
    public void PrevChar_AtStartPlays_Boundary_AndReturnsNull()
    {
        var (cursor, mock) = MakeCursor("abc");
        var ch = cursor.PrevChar();
        Assert.Null(ch);
        mock.Verify(p => p.Play("boundary"), Times.Once);
        Assert.Equal(0, cursor.TextOffset);
    }

    [Fact]
    public void PrevChar_AtStart_WithWrap_PlaysWrap_AndWrapsToEnd()
    {
        var (cursor, mock) = MakeCursor("abc", wrap: true);
        var ch = cursor.PrevChar();
        mock.Verify(p => p.Play("wrap"), Times.Once);
        Assert.Equal('c', ch);
        Assert.Equal(2, cursor.TextOffset);
    }

    // -------------------------------------------------------------------------
    // Word movement — forward
    // -------------------------------------------------------------------------

    [Fact]
    public void NextWord_MovesToStartOfNextWord()
    {
        var (cursor, _) = MakeCursor("hello world foo");
        var word = cursor.NextWord();
        Assert.Equal("world", word);
        Assert.Equal(6, cursor.TextOffset);
    }

    [Fact]
    public void NextWord_AtLastWord_Plays_Boundary()
    {
        var (cursor, mock) = MakeCursor("hello world");
        cursor.NextWord(); // "world"
        var word = cursor.NextWord(); // boundary
        Assert.Null(word);
        mock.Verify(p => p.Play("boundary"), Times.Once);
    }

    [Fact]
    public void NextWord_AtEnd_WithWrap_PlaysWrap()
    {
        var (cursor, mock) = MakeCursor("hello world", wrap: true);
        cursor.NextWord(); // "world"
        var word = cursor.NextWord(); // wraps
        mock.Verify(p => p.Play("wrap"), Times.Once);
        Assert.NotNull(word);
        Assert.Equal(0, cursor.TextOffset);
    }

    // -------------------------------------------------------------------------
    // Word movement — backward
    // -------------------------------------------------------------------------

    [Fact]
    public void PrevWord_MovesToStartOfPreviousWord()
    {
        var (cursor, _) = MakeCursor("hello world foo");
        cursor.NextWord(); // "world" at 6
        cursor.NextWord(); // "foo" at 12
        var word = cursor.PrevWord();
        Assert.Equal("world", word);
        Assert.Equal(6, cursor.TextOffset);
    }

    [Fact]
    public void PrevWord_AtStart_Plays_Boundary()
    {
        var (cursor, mock) = MakeCursor("hello world");
        var word = cursor.PrevWord();
        Assert.Null(word);
        mock.Verify(p => p.Play("boundary"), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Line movement — forward
    // -------------------------------------------------------------------------

    [Fact]
    public void NextLine_MovesToNextLine()
    {
        var (cursor, _) = MakeCursor("line1\nline2\nline3");
        var line = cursor.NextLine();
        Assert.Equal("line2", line);
        Assert.Equal(6, cursor.TextOffset);
    }

    [Fact]
    public void NextLine_AtLastLine_Plays_Boundary()
    {
        var (cursor, mock) = MakeCursor("line1\nline2");
        cursor.NextLine(); // "line2"
        var line = cursor.NextLine(); // boundary
        Assert.Null(line);
        mock.Verify(p => p.Play("boundary"), Times.Once);
    }

    [Fact]
    public void NextLine_AtLastLine_WithWrap_PlaysWrap()
    {
        var (cursor, mock) = MakeCursor("line1\nline2", wrap: true);
        cursor.NextLine(); // "line2"
        var line = cursor.NextLine(); // wraps
        mock.Verify(p => p.Play("wrap"), Times.Once);
        Assert.NotNull(line);
        Assert.Equal(0, cursor.TextOffset);
    }

    // -------------------------------------------------------------------------
    // Line movement — backward
    // -------------------------------------------------------------------------

    [Fact]
    public void PrevLine_MovesToPreviousLine()
    {
        var (cursor, _) = MakeCursor("line1\nline2\nline3");
        cursor.NextLine(); // line2 at 6
        cursor.NextLine(); // line3 at 12
        var line = cursor.PrevLine();
        Assert.Equal("line2", line);
        Assert.Equal(6, cursor.TextOffset);
    }

    [Fact]
    public void PrevLine_AtFirstLine_Plays_Boundary()
    {
        var (cursor, mock) = MakeCursor("line1\nline2");
        var line = cursor.PrevLine();
        Assert.Null(line);
        mock.Verify(p => p.Play("boundary"), Times.Once);
    }

    [Fact]
    public void PrevLine_AtStart_WithWrap_PlaysWrap()
    {
        var (cursor, mock) = MakeCursor("line1\nline2", wrap: true);
        var line = cursor.PrevLine();
        mock.Verify(p => p.Play("wrap"), Times.Once);
        Assert.NotNull(line);
    }

    // -------------------------------------------------------------------------
    // CurrentNode
    // -------------------------------------------------------------------------

    [Fact]
    public void CurrentNode_ReturnsNodeCoveringOffset()
    {
        var (cursor, _) = MakeCursor("hello\nworld");
        // offset 0 — first text node covers it
        Assert.NotNull(cursor.CurrentNode);
    }

    // -------------------------------------------------------------------------
    // SetDocument
    // -------------------------------------------------------------------------

    [Fact]
    public void SetDocument_UpdatesDocumentAndClampsOffset()
    {
        var (cursor, _) = MakeCursor("hello world");
        var newDoc = BuildDoc("hi");
        cursor.SetDocument(newDoc, 999); // clamp to valid range
        Assert.True(cursor.TextOffset < newDoc.FlatText.Length);
    }

    // -------------------------------------------------------------------------
    // ReadLineAt / ReadWordAt helpers
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadLineAt_ReturnsLineWithoutNewline()
    {
        var (cursor, _) = MakeCursor("line1\nline2\nline3");
        Assert.Equal("line1", cursor.ReadLineAt(0));
        Assert.Equal("line2", cursor.ReadLineAt(6));
        Assert.Equal("line3", cursor.ReadLineAt(12));
    }

    [Fact]
    public void ReadWordAt_ReturnsWordText()
    {
        var (cursor, _) = MakeCursor("hello world foo");
        Assert.Equal("hello", cursor.ReadWordAt(0));
        Assert.Equal("world", cursor.ReadWordAt(6));
        Assert.Equal("foo", cursor.ReadWordAt(12));
    }

    // -------------------------------------------------------------------------
    // PrevLine from mid-line, wrap targets, current line/word
    // -------------------------------------------------------------------------

    [Fact]
    public void PrevLine_FromMiddleOfLine_MovesToPreviousLine()
    {
        var (cursor, _) = MakeCursor("line1\nline2\nline3");
        cursor.MoveTo(14); // inside "line3"

        var line = cursor.PrevLine();

        Assert.Equal("line2", line);
        Assert.Equal(6, cursor.TextOffset);
    }

    [Fact]
    public void PrevLine_FromMiddleOfFirstLine_PlaysBoundary()
    {
        var (cursor, mock) = MakeCursor("line1\nline2");
        cursor.MoveTo(3);

        Assert.Null(cursor.PrevLine());
        mock.Verify(p => p.Play("boundary"), Times.Once);
    }

    [Fact]
    public void PrevLine_OverEmptyLine_StopsOnIt()
    {
        var (cursor, _) = MakeCursor("a\n\nb");
        cursor.MoveTo(3); // "b"

        Assert.Equal("", cursor.PrevLine());
        Assert.Equal(2, cursor.TextOffset);
        Assert.Equal("a", cursor.PrevLine());
    }

    [Fact]
    public void PrevLine_AtStart_WithWrap_MovesToStartOfLastLine()
    {
        var (cursor, _) = MakeCursor("line1\nline2\n", wrap: true);

        Assert.Equal("line2", cursor.PrevLine());
        Assert.Equal(6, cursor.TextOffset);
    }

    [Fact]
    public void PrevWord_AtStart_WithWrap_ReturnsLastWord()
    {
        var (cursor, _) = MakeCursor("hello big world", wrap: true);

        Assert.Equal("world", cursor.PrevWord());
    }

    [Fact]
    public void ReadCurrentLine_And_Word_FromMidLine()
    {
        var (cursor, _) = MakeCursor("first line\nsecond line");
        cursor.MoveTo(14); // "second" -> 'c'

        Assert.Equal("second line", cursor.ReadCurrentLine());
        Assert.Equal("second", cursor.ReadCurrentWord());
    }

    [Fact]
    public void PlayCuesFalse_BoundaryIsSilent()
    {
        var (cursor, mock) = MakeCursor("only line");
        cursor.PlayCues = false;

        Assert.Null(cursor.NextLine());
        mock.Verify(p => p.Play(It.IsAny<string>()), Times.Never);
    }
}

public class VBufferCursorLineLengthTests
{
    private static VBufferCursor CursorFor(string flatText, int maxLineLength = 20)
    {
        var root = new VBufferNode { Id = 0, UIARuntimeId = [1], ControlType = "Document", TextRange = (0, flatText.Length) };
        var doc = new VBufferDocument(flatText, root, [root]);
        return new VBufferCursor(doc, new Moq.Mock<Vox.Core.Audio.IAudioCuePlayer>().Object) { MaxLineLength = maxLineLength };
    }

    // 44 characters of words, then a short line
    private const string Text = "alpha beta gamma delta epsilon zeta eta theta\nshort\n";

    [Fact]
    public void LongLine_IsReadInPiecesAtWordBoundaries()
    {
        var cursor = CursorFor(Text);

        Assert.Equal("alpha beta gamma", cursor.ReadCurrentLine());
        Assert.Equal("delta epsilon zeta", cursor.NextLine());
        Assert.Equal("eta theta", cursor.NextLine());
        Assert.Equal("short", cursor.NextLine());
    }

    [Fact]
    public void PrevLine_WalksBackThroughThePieces()
    {
        var cursor = CursorFor(Text);
        cursor.NextLine(); cursor.NextLine(); cursor.NextLine(); // "short"

        Assert.Equal("eta theta", cursor.PrevLine());
        Assert.Equal("delta epsilon zeta", cursor.PrevLine());
        Assert.Equal("alpha beta gamma", cursor.PrevLine());
        Assert.Null(cursor.PrevLine());
    }

    [Fact]
    public void ZeroMaxLength_KeepsWholeLines()
    {
        var cursor = CursorFor(Text, maxLineLength: 0);

        Assert.Equal("alpha beta gamma delta epsilon zeta eta theta", cursor.ReadCurrentLine());
        Assert.Equal("short", cursor.NextLine());
    }

    [Fact]
    public void OneLongWord_IsSplitAtTheLimit()
    {
        var cursor = CursorFor(new string('x', 30) + "\n", maxLineLength: 20);

        Assert.Equal(new string('x', 20), cursor.ReadCurrentLine());
        Assert.Equal(new string('x', 10), cursor.NextLine());
    }

    [Fact]
    public void HomeAndEnd_StayWithinThePiece()
    {
        var cursor = CursorFor(Text);
        cursor.NextLine(); // "delta epsilon zeta"

        Assert.Equal('a', cursor.EndOfLine());
        Assert.Equal("delta epsilon zeta".Length - 1, cursor.TextOffset - Text.IndexOf("delta"));
        Assert.Equal('d', cursor.StartOfLine());
        Assert.Equal(Text.IndexOf("delta"), cursor.TextOffset);
    }

    [Fact]
    public void EmptyLine_HomeAndEndReturnNull()
    {
        var cursor = CursorFor("a\n\nb\n");
        cursor.NextLine(); // blank line

        Assert.Null(cursor.EndOfLine());
        Assert.Null(cursor.StartOfLine());
    }

    [Fact]
    public void Paragraphs_SkipBlankLinesAndPieces()
    {
        var cursor = CursorFor("alpha beta gamma delta epsilon zeta\n\nsecond\nthird\n");

        Assert.Equal("second", cursor.NextParagraph());
        Assert.Equal("third", cursor.NextParagraph());
        Assert.Null(cursor.NextParagraph());
        Assert.Equal("second", cursor.PrevParagraph());
        Assert.Equal("alpha beta gamma delta epsilon zeta", cursor.PrevParagraph());
        Assert.Null(cursor.PrevParagraph());
    }

    [Fact]
    public void TopAndBottom()
    {
        var cursor = CursorFor(Text);

        Assert.Equal("short", cursor.BottomOfDocument());
        Assert.Equal("alpha beta gamma", cursor.TopOfDocument());
        Assert.Equal(0, cursor.TextOffset);
    }
}
