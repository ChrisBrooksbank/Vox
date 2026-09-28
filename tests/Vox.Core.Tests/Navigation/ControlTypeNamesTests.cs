using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class ControlTypeNamesTests
{
    [Theory]
    [InlineData("Hyperlink", "link")]
    [InlineData("ComboBox", "combo box")]
    [InlineData("Button", "button")]
    [InlineData("Text", null)]
    [InlineData("Group", null)]
    [InlineData("", null)]
    public void ToSpoken(string controlType, string? expected)
    {
        Assert.Equal(expected, ControlTypeNames.ToSpoken(controlType));
    }

    [Fact]
    public void ChromiumHeading_IsNotAnnouncedAsText()
    {
        var node = new VBufferNode { Name = "Welcome", ControlType = "Text", HeadingLevel = 1 };

        var text = new AnnouncementBuilder().Build(node, VerbosityLevel.Beginner);

        Assert.Equal("heading level 1, Welcome", text);
    }

    [Fact]
    public void FocusAnnouncement_UsesSpokenNamesAndVerbosity()
    {
        var focus = new FocusChangedEvent(DateTimeOffset.UtcNow, "Home", "Hyperlink", IsLink: true);

        Assert.Equal("Home, link", new AnnouncementBuilder().Build(focus, VerbosityProfile.Beginner, true));
        Assert.Equal("Home", new AnnouncementBuilder().Build(focus, VerbosityProfile.Advanced, true));
    }
}
