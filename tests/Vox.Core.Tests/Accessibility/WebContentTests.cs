using Vox.Core.Accessibility;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class WebContentTests
{
    [Theory]
    [InlineData("Chrome", false, true)]
    [InlineData("Gecko", false, true)]
    [InlineData("Win32", false, false)]
    [InlineData("", false, false)]
    [InlineData("MSAA", true, true)]   // Firefox through the MSAA proxy
    [InlineData("", true, true)]
    [InlineData("Win32", true, false)] // the Firefox window itself
    public void IsWebElement(string frameworkId, bool insideFirefox, bool expected) =>
        Assert.Equal(expected, WebContent.IsWebElement(frameworkId, insideFirefox));

    [Theory]
    [InlineData("MozillaWindowClass", true)]
    [InlineData("MozillaDialogClass", true)]
    [InlineData("Chrome_WidgetWin_1", false)]
    [InlineData(null, false)]
    public void IsFirefoxWindowClass(string? className, bool expected) =>
        Assert.Equal(expected, WebContent.IsFirefoxWindowClass(className));

    [Theory]
    [InlineData("heading", 2)]
    [InlineData("Heading 3", 3)]
    [InlineData("heading level 1", 1)]
    [InlineData("heading 9", 2)]
    [InlineData("text", 0)]
    [InlineData("headings list", 0)]
    [InlineData(null, 0)]
    public void HeadingLevelFromLocalizedType(string? type, int expected) =>
        Assert.Equal(expected, WebContent.HeadingLevelFromLocalizedType(type));
}
