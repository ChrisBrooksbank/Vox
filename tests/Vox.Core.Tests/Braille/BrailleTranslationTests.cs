using Vox.Core.Braille;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Braille;

public class BrailleTranslationTests
{
    [Fact]
    public void FakeTranslator_WritesOneCellPerCharacterWhenUncontracted()
    {
        var translation = new FakeBrailleTranslator().Translate("Hi 5.");

        Assert.Equal("⡓⠊⠀⠢⠨", BrailleDots.ToUnicode(translation.Cells));
        Assert.Equal([0, 1, 2, 3, 4], translation.CellForChar);
        Assert.Equal([0, 1, 2, 3, 4], translation.CharForCell);
        Assert.Null(translation.CursorCell);
    }

    [Fact]
    public void FakeTranslator_ContractsWholeWordsOnly()
    {
        var translator = new FakeBrailleTranslator { Contracted = true };

        var translation = translator.Translate("the theme");

        // "the" is one cell; "theme" is spelled out.
        Assert.Equal(7, translation.Cells.Length);
        Assert.Equal([0, 0, 0, 1, 2, 3, 4, 5, 6], translation.CellForChar);
        Assert.Equal([0, 3, 4, 5, 6, 7, 8], translation.CharForCell);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0)] // inside "the"
    [InlineData(4, 2)] // the start of "and", after "the" and the space
    [InlineData(7, 3)] // the end of the text: the cell after the last
    public void CursorOffset_MapsToItsCell(int offset, int cell)
    {
        var translator = new FakeBrailleTranslator { Contracted = true };

        var translation = translator.Translate("the and", offset);

        Assert.Equal(cell, translation.CursorCell);
    }

    [Fact]
    public void CellAndOffset_MapBothWaysAndClampAtTheEnds()
    {
        var translation = new FakeBrailleTranslator { Contracted = true }.Translate("go and see");

        Assert.Equal(3, translation.CellOf(3));   // 'a' of "and" → the contraction cell
        Assert.Equal(3, translation.CellOf(5));   // 'd' of "and" → the same cell
        Assert.Equal(3, translation.OffsetOf(3)); // the contraction cell → the start of "and"
        Assert.Equal(0, translation.CellOf(-1));
        Assert.Equal(translation.Cells.Length, translation.CellOf(100));
        Assert.Equal(0, translation.OffsetOf(-1));
        Assert.Equal(10, translation.OffsetOf(100));
    }

    [Fact]
    public void FakeTranslator_BackTranslatesWhatItWrote()
    {
        var translator = new FakeBrailleTranslator { Contracted = true };

        var cells = translator.Translate("Read the 2 notes.").Cells;

        Assert.Equal("Read the 2 notes.", translator.BackTranslate(cells));
        Assert.Equal("?", translator.BackTranslate(new[] { FakeBrailleTranslator.UnknownCell }));
    }

    [Fact]
    public void Empty_HasNoCells()
    {
        Assert.Empty(BrailleTranslation.Empty.Cells);
        Assert.Equal(0, BrailleTranslation.Empty.CellOf(0));
        Assert.Equal(0, BrailleTranslation.Empty.OffsetOf(0));
    }
}
