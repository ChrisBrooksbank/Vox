using Vox.Core.Accessibility;
using Vox.Core.Navigation;
using Vox.Core.Text;
using Xunit;

namespace Vox.Core.Tests.Text;

public class TextFormattingTests
{
    [Fact]
    public void Describe_EverythingKnown()
    {
        var attributes = new TextAttributes
        {
            FontName = "Calibri", FontSize = 11, IsBold = true, IsItalic = true, IsUnderline = true,
            ForegroundColor = "#FF0000", IsSpellingError = true,
        };

        Assert.Equal("Calibri, 11 point, bold, italic, underlined, red, misspelled", TextFormatting.Describe(attributes));
    }

    [Fact]
    public void Describe_HalfPointSize_And_PlainText()
    {
        Assert.Equal("Arial, 10.5 point, black",
            TextFormatting.Describe(new TextAttributes { FontName = "Arial", FontSize = 10.5, IsBold = false, ForegroundColor = "#000000" }));
    }

    [Fact]
    public void Describe_NothingKnown()
    {
        Assert.Equal("No formatting information", TextFormatting.Describe(TextAttributes.Unknown));
    }

    [Theory]
    [InlineData("#0563C1", "blue")]       // Word's hyperlink blue
    [InlineData("#FFFFFF", "white")]
    [InlineData("#00B050", "green")]
    [InlineData("nonsense", null)]
    public void ColorName_Nearest(string hex, string? expected)
    {
        Assert.Equal(expected, TextFormatting.ColorName(hex));
    }

    [Fact]
    public void FocusedTextMonitor_DescribeFormatting_UsesTheAttributesAtTheCaret()
    {
        var document = new StringTextDocument("ab", 1, attributesAt: i => new TextAttributes { IsBold = i == 1 });

        Assert.Equal("bold", FocusedTextMonitor.Describe(document, TextReadKind.Formatting));
    }
}
