using Vox.Core.Braille;
using Xunit;

namespace Vox.Core.Tests.Braille;

public class BrailleDotsTests
{
    [Theory]
    [InlineData("", 0x00)]
    [InlineData("1", 0x01)]
    [InlineData("125", 0x13)]
    [InlineData("78", 0xC0)]
    [InlineData("12345678", 0xFF)]
    public void FromDots_RaisesOneBitPerDot(string dots, int expected)
    {
        Assert.Equal((byte)expected, BrailleDots.FromDots(dots));
    }

    [Fact]
    public void FromDots_RejectsWhatIsNotADot()
    {
        Assert.Throws<ArgumentException>(() => BrailleDots.FromDots("19"));
    }

    [Fact]
    public void ToDots_ListsRaisedDotsInOrder()
    {
        Assert.Equal("125", BrailleDots.ToDots(BrailleDots.FromDots("521")));
        Assert.Equal(string.Empty, BrailleDots.ToDots(0));
    }

    [Fact]
    public void Unicode_RoundTripsEveryCell()
    {
        var cells = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

        var unicode = BrailleDots.ToUnicode(cells);

        Assert.Equal('⠀', unicode[0]);
        Assert.Equal('⣿', unicode[^1]);
        Assert.Equal(cells, BrailleDots.FromUnicode(unicode));
    }

    [Fact]
    public void FromUnicode_ReadsASpaceAsABlankCell()
    {
        Assert.Equal(new byte[] { 0x01, 0x00, 0x03 }, BrailleDots.FromUnicode("⠁ ⠃"));
        Assert.Throws<ArgumentException>(() => BrailleDots.FromUnicode("a"));
    }
}
