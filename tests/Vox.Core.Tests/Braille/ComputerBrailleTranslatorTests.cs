using Vox.Core.Braille;
using Xunit;

namespace Vox.Core.Tests.Braille;

public class ComputerBrailleTranslatorTests
{
    private readonly ComputerBrailleTranslator _translator = new();

    [Theory]
    [InlineData('a', "1")]
    [InlineData('h', "125")]
    [InlineData('z', "1356")]
    [InlineData('A', "17")]
    [InlineData('Z', "13567")]
    [InlineData(' ', "")]
    [InlineData('0', "356")]
    [InlineData('1', "2")]
    [InlineData('9', "35")]
    [InlineData('.', "46")]
    [InlineData(',', "6")]
    [InlineData('!', "2346")]
    [InlineData('?', "1456")]
    [InlineData('-', "36")]
    [InlineData('/', "34")]
    [InlineData('#', "3456")]
    [InlineData('=', "123456")]
    [InlineData('@', "47")]
    [InlineData('`', "4")]
    [InlineData('[', "2467")]
    [InlineData('{', "246")]
    [InlineData('\\', "12567")]
    [InlineData('|', "1256")]
    [InlineData('_', "456")]
    [InlineData('\x7F', "4567")]
    [InlineData('~', "45")]
    [InlineData('\t', "2478")]
    public void Translate_WritesNabccCells(char c, string dots)
    {
        var translation = _translator.Translate(c.ToString());

        Assert.Equal(dots, BrailleDots.ToDots(Assert.Single(translation.Cells)));
    }

    [Fact]
    public void EveryAsciiCharacterHasItsOwnCell()
    {
        var cells = Enumerable.Range(0, 128).Select(c => ComputerBrailleTranslator.CellOf((char)c)).ToList();

        Assert.Equal(128, cells.Distinct().Count());
        Assert.DoesNotContain(ComputerBrailleTranslator.UnknownCell, cells);
    }

    [Fact]
    public void Translate_IsOneCellPerCharacter()
    {
        var translation = _translator.Translate("Hi, 42!", cursorOffset: 3);

        Assert.Equal("⡓⠊⠠⠀⠲⠆⠮", BrailleDots.ToUnicode(translation.Cells));
        Assert.Equal(Enumerable.Range(0, 7), translation.CellForChar);
        Assert.Equal(Enumerable.Range(0, 7), translation.CharForCell);
        Assert.Equal(3, translation.CursorCell);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(9, 5)]
    [InlineData(-1, 0)]
    public void Translate_ReportsTheCursorCell(int? offset, int? cell)
    {
        Assert.Equal(cell, _translator.Translate("hello", offset).CursorCell);
    }

    [Fact]
    public void Translate_EmptyText()
    {
        var translation = _translator.Translate(string.Empty, cursorOffset: 0);

        Assert.Empty(translation.Cells);
        Assert.Equal(0, translation.CursorCell);
    }

    [Fact]
    public void NonAsciiCharacters_GetTheUnknownCell_AndBackTranslateToQuestionMark()
    {
        var translation = _translator.Translate("é€");

        Assert.Equal(new[] { ComputerBrailleTranslator.UnknownCell, ComputerBrailleTranslator.UnknownCell }, translation.Cells);
        Assert.Equal("??", _translator.BackTranslate(translation.Cells));
    }

    [Fact]
    public void BackTranslate_InvertsTranslate_ForAllAscii()
    {
        var ascii = new string(Enumerable.Range(0, 128).Select(c => (char)c).ToArray());

        var cells = _translator.Translate(ascii).Cells;

        Assert.Equal(ascii, _translator.BackTranslate(cells));
    }

    [Fact]
    public void BackTranslate_UnusedCellIsQuestionMark()
    {
        Assert.Equal("a?", _translator.BackTranslate(new byte[] { BrailleDots.Dot1, BrailleDots.Dot8 }));
    }
}
