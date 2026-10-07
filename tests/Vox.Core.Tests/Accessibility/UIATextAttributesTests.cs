using Vox.Core.Accessibility;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class UIATextAttributesTests
{
    [Fact]
    public void FromValues_MapsUiaValues()
    {
        var attributes = UIATextAttributes.FromValues(
            fontName: "Calibri", fontSize: 11.0, fontWeight: 700, isItalic: true, underlineStyle: 1,
            foregroundColor: 0x0000FF, annotationTypes: new[] { 60001 }, culture: 1033);

        Assert.Equal("Calibri", attributes.FontName);
        Assert.Equal(11.0, attributes.FontSize);
        Assert.True(attributes.IsBold);
        Assert.True(attributes.IsItalic);
        Assert.True(attributes.IsUnderline);
        Assert.Equal("#FF0000", attributes.ForegroundColor);
        Assert.True(attributes.IsSpellingError);
        Assert.False(attributes.IsGrammarError);
        Assert.Equal("en-US", attributes.Language);
    }

    [Fact]
    public void FromValues_UnknownOrMixedValues_AreNull()
    {
        var attributes = UIATextAttributes.FromValues(null, null, null, null, null, null, null, null);

        Assert.Null(attributes.FontName);
        Assert.Null(attributes.IsBold);
        Assert.Null(attributes.IsItalic);
        Assert.False(attributes.IsSpellingError);
        Assert.Null(attributes.Language);
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(600, true)]
    [InlineData(700, true)]
    public void FromValues_FontWeight(int weight, bool bold)
    {
        Assert.Equal(bold, UIATextAttributes.FromValues(null, null, weight, null, null, null, null, null).IsBold);
    }

    [Fact]
    public void FromValues_GrammarErrorAnnotation()
    {
        var attributes = UIATextAttributes.FromValues(null, null, null, null, null, null, new object[] { 60002 }, null);

        Assert.True(attributes.IsGrammarError);
        Assert.False(attributes.IsSpellingError);
    }
}
