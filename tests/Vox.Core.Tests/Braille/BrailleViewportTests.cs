using Vox.Core.Braille;
using Vox.Core.Configuration;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Braille;

public class BrailleViewportTests
{
    private readonly FakeBrailleTranslator _translator = new();
    private static readonly BrailleViewOptions Options = new();

    private BrailleViewport Viewport() => new(_translator);

    private static BrailleLine Edit(string text, int? cursor = null, (int, int)? selection = null) => new()
    {
        Kind = BrailleLineKind.EditLine,
        Text = text,
        CursorOffset = cursor,
        Selection = selection,
        SourceEnd = text.Length,
    };

    /// <summary>The line's text in the window (the fake translator has one cell per character).</summary>
    private static string Window(BrailleViewport viewport) =>
        viewport.Line!.Text[viewport.WindowStart..viewport.WindowEnd];

    // "hello world foo" on 8 cells, word wrap on: "hello " | "world " | "foo"
    private const string Words = "hello world foo";

    [Fact]
    public void Show_WithoutCursor_StartsAtTheLineStart()
    {
        var viewport = Viewport();

        viewport.Show(Edit(Words), 8, Options);

        Assert.Equal(0, viewport.WindowStart);
        Assert.Equal("hello ", Window(viewport));
        Assert.Equal(8, viewport.CellCount);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(7, 6)]
    [InlineData(13, 12)]
    [InlineData(15, 12)]
    public void Show_WithCursor_ShowsTheCursorsWindow(int cursor, int windowStart)
    {
        var viewport = Viewport();

        viewport.Show(Edit(Words, cursor), 8, Options);

        Assert.Equal(windowStart, viewport.WindowStart);
        Assert.Equal(cursor - windowStart, viewport.CursorCellInWindow);
    }

    [Fact]
    public void Show_CursorOnBlanksPanningSkips_StillShowsTheCursor()
    {
        // "hello " | "world" on 6 cells: panning right skips the blanks at 6 and 7
        var viewport = Viewport();

        viewport.Show(Edit("hello   world", 7), 6, Options);

        Assert.Equal(7, viewport.WindowStart);
        Assert.Equal(0, viewport.CursorCellInWindow);
    }

    [Fact]
    public void Pan_RightThenLeft_GoesThroughTheSameWindows()
    {
        var viewport = Viewport();
        viewport.Show(Edit(Words), 8, Options);

        var right = new List<string> { Window(viewport) };
        while (viewport.PanRight())
            right.Add(Window(viewport));
        var left = new List<string> { Window(viewport) };
        while (viewport.PanLeft())
            left.Add(Window(viewport));

        Assert.Equal(["hello ", "world ", "foo"], right);
        left.Reverse();
        Assert.Equal(right, left);
        Assert.Equal(0, viewport.WindowStart);
    }

    [Fact]
    public void Pan_AtTheEdges_ReturnsFalseAndStays()
    {
        var viewport = Viewport();
        viewport.Show(Edit("short"), 8, Options);

        Assert.False(viewport.PanLeft());
        Assert.False(viewport.PanRight());
        Assert.Equal(0, viewport.WindowStart);
    }

    [Fact]
    public void WordWrap_NeverSplitsAWord()
    {
        var text = "the quick brown fox jumps over the lazy dog again and again";
        var viewport = Viewport();
        viewport.Show(Edit(text), 10, Options);

        var shown = new List<(int Start, int End)>();
        do
            shown.Add((viewport.WindowStart, viewport.WindowEnd));
        while (viewport.PanRight());

        foreach (var (start, end) in shown)
        {
            Assert.True(end - start <= 10);
            Assert.True(start == 0 || text[start - 1] == ' ', $"window at {start} starts inside a word");
            Assert.True(end == text.Length || text[end] == ' ' || text[end - 1] == ' ', $"window ending at {end} cuts a word");
        }
        // Every word is shown
        var joined = string.Concat(shown.Select(w => text[w.Start..w.End]));
        Assert.Equal(text.Replace(" ", ""), joined.Replace(" ", ""));
    }

    [Fact]
    public void WordWrap_WordLongerThanTheDisplay_IsCut()
    {
        var viewport = Viewport();
        viewport.Show(Edit("abcdefghijklmnop qr"), 8, Options);

        Assert.Equal("abcdefgh", Window(viewport));
        Assert.True(viewport.PanRight());
        Assert.Equal("ijklmnop", Window(viewport));
    }

    [Fact]
    public void WordWrapOff_FillsTheDisplay()
    {
        var viewport = Viewport();
        viewport.Show(Edit(Words), 8, Options with { WordWrap = false });

        Assert.Equal("hello wo", Window(viewport));
        Assert.True(viewport.PanRight());
        Assert.Equal("rld foo", Window(viewport));
    }

    [Fact]
    public void ShowLineEnd_ShowsTheLastWindow()
    {
        var viewport = Viewport();
        viewport.Show(Edit(Words), 8, Options);

        viewport.ShowLineEnd();

        Assert.Equal("foo", Window(viewport));
        Assert.True(viewport.PanLeft());
        Assert.Equal("world ", Window(viewport));
    }

    [Fact]
    public void FillBeforeAnchor_ShowsContextEndingWithTheFocus()
    {
        // "Settings dlg OK btn", the focus "OK btn" at 13
        var line = new BrailleLine
        {
            Kind = BrailleLineKind.Focus, Text = "Settings dlg OK btn", AnchorOffset = 13,
            AnchorMode = BrailleAnchorMode.FillBeforeAnchor, SourceEnd = 19,
        };
        var viewport = Viewport();

        viewport.Show(line, 10, Options);
        Assert.Equal("dlg OK btn", Window(viewport));

        // Less room: the context starts at a word, never before the focus is cut off
        viewport.Show(line, 8, Options);
        Assert.Equal("OK btn", Window(viewport));

        // Room for everything
        viewport.Show(line, 40, Options);
        Assert.Equal(0, viewport.WindowStart);
    }

    [Fact]
    public void AtAnchor_StartsAtTheFocus_AndPanningLeftShowsTheContext()
    {
        var line = new BrailleLine
        {
            Kind = BrailleLineKind.Focus, Text = "Settings dlg OK btn", AnchorOffset = 13,
            AnchorMode = BrailleAnchorMode.AtAnchor, SourceEnd = 19,
        };
        var viewport = Viewport();

        viewport.Show(line, 10, Options);

        Assert.Equal("OK btn", Window(viewport));
        Assert.True(viewport.PanLeft());
        Assert.True(viewport.WindowStart < 13);
    }

    [Theory]
    [InlineData(BrailleCursorShape.Underline, BrailleDots.Dots78)]
    [InlineData(BrailleCursorShape.Dot8, BrailleDots.Dot8)]
    [InlineData(BrailleCursorShape.AllDots, 0xFF)]
    public void Render_DrawsTheCursorShape(BrailleCursorShape shape, byte dots)
    {
        var viewport = Viewport();
        viewport.Show(Edit("ab cd", 1), 8, Options with { CursorShape = shape });

        var cells = viewport.Render(cursorVisible: true);

        Assert.Equal(8, cells.Length);
        Assert.Equal((byte)(BrailleDots.FromDots("12") | dots), cells[1]);
        Assert.Equal(BrailleDots.FromDots("1"), cells[0]);
        Assert.Equal(0, cells[5]);
    }

    [Fact]
    public void Render_CursorHidden_LeavesTheCellAlone()
    {
        var viewport = Viewport();
        viewport.Show(Edit("ab cd", 1), 8, Options);

        var cells = viewport.Render(cursorVisible: false);

        Assert.Equal(BrailleDots.FromDots("12"), cells[1]);
    }

    [Fact]
    public void Render_CursorAtTheLineEnd_IsOnTheBlankCellAfterIt()
    {
        var viewport = Viewport();
        viewport.Show(Edit("ab", 2), 8, Options);

        var cells = viewport.Render(cursorVisible: true);

        Assert.Equal(2, viewport.CursorCellInWindow);
        Assert.Equal(BrailleDots.Dots78, cells[2]);
    }

    [Fact]
    public void Render_DrawsSelectionWithDots78()
    {
        var viewport = Viewport();
        viewport.Show(Edit("abcdef", null, (1, 4)), 8, Options);

        var cells = viewport.Render(cursorVisible: true);

        Assert.Equal(0, cells[0] & BrailleDots.Dots78);
        Assert.Equal(BrailleDots.Dots78, cells[1] & BrailleDots.Dots78);
        Assert.Equal(BrailleDots.Dots78, cells[3] & BrailleDots.Dots78);
        Assert.Equal(0, cells[4] & BrailleDots.Dots78);
    }

    [Fact]
    public void Render_SelectionOff_DrawsNoDots78()
    {
        var viewport = Viewport();
        viewport.Show(Edit("abcdef", null, (1, 4)), 8, Options with { ShowSelection = false });

        Assert.All(viewport.Render(cursorVisible: true), cell => Assert.Equal(0, cell & BrailleDots.Dots78));
    }

    [Fact]
    public void Render_ShowsOnlyTheWindow_PaddedToTheDisplay()
    {
        var viewport = Viewport();
        viewport.Show(Edit(Words), 8, Options);
        viewport.PanRight();

        var cells = viewport.Render(cursorVisible: true);

        Assert.Equal("⠺⠕⠗⠇⠙", BrailleDots.ToUnicode(cells).TrimEnd('⠀'));
    }

    [Fact]
    public void Render_BeforeAnyLine_IsBlank()
    {
        Assert.Empty(Viewport().Render(cursorVisible: true));
    }

    [Fact]
    public void TextOffsetAtCell_MapsRoutingKeysToTheLine()
    {
        var viewport = Viewport();
        viewport.Show(Edit(Words), 8, Options);
        viewport.PanRight(); // "world "

        Assert.Equal(6, viewport.TextOffsetAtCell(0));
        Assert.Equal(10, viewport.TextOffsetAtCell(4));
        // Past what the window shows (the next word is in the next window)
        Assert.Null(viewport.TextOffsetAtCell(7));
        Assert.Null(viewport.TextOffsetAtCell(-1));
        Assert.Null(viewport.TextOffsetAtCell(8));
    }

    [Fact]
    public void TextOffsetAtCell_TheCellAfterTheLineEnd_IsTheLineEnd()
    {
        var viewport = Viewport();
        viewport.Show(Edit(Words), 8, Options);
        viewport.ShowLineEnd(); // "foo"

        Assert.Equal(12, viewport.TextOffsetAtCell(0));
        Assert.Equal(15, viewport.TextOffsetAtCell(3));
        Assert.Null(viewport.TextOffsetAtCell(4));
    }

    [Fact]
    public void TextOffsetAtCell_WithContractions_GivesTheWordsFirstCharacter()
    {
        _translator.Contracted = true;
        var viewport = Viewport();
        viewport.Show(Edit("see the cat"), 20, Options);

        // "see " then "the" as one cell, then " cat"
        Assert.Equal(4, viewport.TextOffsetAtCell(4));
        Assert.Equal(7, viewport.TextOffsetAtCell(5));
        Assert.Equal(11, viewport.TextOffsetAtCell(9));
    }

    [Fact]
    public void KeepWindow_KeepsTheWindowWhileItShowsTheCursor()
    {
        // "aaaa bbbb cccc" on 8 cells: "aaaa " | "bbbb " | "cccc"; the cursor in "cccc"
        var viewport = Viewport();
        viewport.Show(Edit("aaaa bbbb cccc", 12), 8, Options);
        Assert.Equal(10, viewport.WindowStart);

        // A character typed at the start moves the words along; the window stays put
        viewport.Show(Edit("xaaaa bbbb cccc", 13), 8, Options, keepWindow: true);
        Assert.Equal(10, viewport.WindowStart);

        // Without keepWindow it is laid out again from the line's start
        viewport.Show(Edit("xaaaa bbbb cccc", 13), 8, Options);
        Assert.Equal(11, viewport.WindowStart);
    }

    [Fact]
    public void KeepWindow_MovesWhenTheCursorLeavesTheWindow()
    {
        var viewport = Viewport();
        viewport.Show(Edit(Words, 13), 8, Options);

        viewport.Show(Edit(Words, 2), 8, Options, keepWindow: true);

        Assert.Equal(0, viewport.WindowStart);
    }

    [Fact]
    public void Resize_KeepsTheCursorShown()
    {
        var viewport = Viewport();
        viewport.Show(Edit(Words, 13), 8, Options);

        viewport.Resize(40);

        Assert.Equal(40, viewport.CellCount);
        Assert.Equal(0, viewport.WindowStart);
        Assert.Equal(13, viewport.CursorCellInWindow);

        viewport.Resize(6);
        Assert.Equal(12, viewport.WindowStart);
        Assert.Equal(1, viewport.CursorCellInWindow);
    }

    [Fact]
    public void ZeroCells_ShowsNothing()
    {
        var viewport = Viewport();
        viewport.Show(Edit(Words, 3), 0, Options);

        Assert.Empty(viewport.Render(cursorVisible: true));
        Assert.False(viewport.PanRight());
        Assert.Null(viewport.TextOffsetAtCell(0));
    }

    [Fact]
    public void ExpandWordAtCursor_IsAskedOnlyWithACursor()
    {
        _translator.Contracted = true;
        var viewport = Viewport();

        viewport.Show(Edit("the cat", 1), 20, Options);
        Assert.Equal(7, viewport.Translation.Cells.Length); // "the" written out at the cursor

        viewport.Show(Edit("the cat"), 20, Options);
        Assert.Equal(5, viewport.Translation.Cells.Length);

        viewport.Show(Edit("the cat", 1), 20, Options with { ExpandWordAtCursor = false });
        Assert.Equal(5, viewport.Translation.Cells.Length);
    }
}
