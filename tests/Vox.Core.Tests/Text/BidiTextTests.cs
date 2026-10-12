using Vox.Core.Braille;
using Vox.Core.Buffer;
using Vox.Core.Tests.Buffer;
using Vox.Core.Tests.TestSupport;
using Vox.Core.Text;
using Xunit;

namespace Vox.Core.Tests.Text;

public class BidiTextTests
{
    // "שלום" (Hebrew) with a right-to-left mark, then " ok"
    private const string Mixed = "שלום‏ ok";

    [Theory]
    [InlineData("שלום world", true)]
    [InlineData("مرحبا", true)]
    [InlineData("123 hello ש", false)]
    [InlineData("123 !", false)]
    public void IsRightToLeft_ByTheFirstStrongLetter(string text, bool rightToLeft) =>
        Assert.Equal(rightToLeft, BidiText.IsRightToLeft(text));

    [Fact]
    public void WithoutControls_MapsBackToTheSource()
    {
        var shown = BidiText.WithoutControls("a‎b⁧c⁩", out var offsets);

        Assert.Equal("abc", shown);
        Assert.Equal([0, 2, 4], offsets!);
        Assert.Equal("plain", BidiText.WithoutControls("plain", out var none));
        Assert.Null(none);
    }

    [Fact]
    public void Braille_LeavesOutTheMarks_InLogicalOrder_WithCursorAndRoutingOnTheRightCharacters()
    {
        var translator = new ComputerBrailleTranslator();

        var translation = BidiText.Translate(translator, Mixed, cursorOffset: 5, expandWordAtCursor: false);

        // Seven cells: the four Hebrew letters, the space, "o", "k"
        Assert.Equal(7, translation.Cells.Length);
        // The cursor after the mark is on the space, the fifth cell
        Assert.Equal(4, translation.CursorCell);
        // Routing the fifth cell lands on the space, offset 5 in the source
        Assert.Equal(5, translation.OffsetOf(4));
        // The mark itself shares the cell after it
        Assert.Equal(4, translation.CellOf(4));
        Assert.Equal(Mixed.Length, translation.CellForChar.Length);
    }

    [Fact]
    public void Braille_TextWithoutMarks_IsTheTranslatorsOwn()
    {
        var translator = new ComputerBrailleTranslator();

        Assert.Equal(translator.Translate("abc", 1).Cells, BidiText.Translate(translator, "abc", 1, false).Cells);
    }

    [Fact]
    public void Buffer_HasNoDirectionMarks()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        root.AddChild(new MockElement { RuntimeId = [2], Name = Mixed });

        var document = new VBufferBuilder().Build(root);

        Assert.DoesNotContain(document.FlatText, BidiText.IsControl);
        Assert.Contains("שלום ok", document.FlatText);
    }
}
