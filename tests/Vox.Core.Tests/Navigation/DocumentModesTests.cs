using Vox.Core.Buffer;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Tests.Buffer;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class DocumentModesTests
{
    private static VBufferDocument Page(MockElement content) =>
        new VBufferBuilder().Build(new MockElement { RuntimeId = [1], ControlType = "Document" }.AddChild(content));

    private static MockElement Text(int id, string name) => new() { RuntimeId = [id], Name = name };

    [Fact]
    public void OrdinaryPage_StartsInBrowseMode()
    {
        var document = Page(new MockElement { RuntimeId = [2], ControlType = "Group" }.AddChild(Text(3, "An article")));

        Assert.Equal(InteractionMode.Browse, DocumentModes.Initial(document, "msedge", null));
    }

    [Fact]
    public void ApplicationRoleAroundThePage_StartsInFocusMode()
    {
        var app = new MockElement { RuntimeId = [2], ControlType = "Group", AriaRole = "application" }
            .AddChild(Text(3, "Inbox"))
            .AddChild(Text(4, "Message list"));
        var document = Page(new MockElement { RuntimeId = [5], ControlType = "Group" }.AddChild(app));

        Assert.True(DocumentModes.IsApplication(document));
        Assert.Equal(InteractionMode.Focus, DocumentModes.Initial(document, "ms-teams", null));
    }

    [Fact]
    public void SmallApplicationWidget_DoesNotMakeThePageAnApplication()
    {
        var page = new MockElement { RuntimeId = [2], ControlType = "Group" }
            .AddChild(Text(3, "A long article about maps and how they are drawn by hand."))
            .AddChild(new MockElement { RuntimeId = [4], ControlType = "Group", AriaRole = "application" }.AddChild(Text(5, "Map")));

        Assert.False(DocumentModes.IsApplication(Page(page)));
    }

    [Fact]
    public void AppSetting_OverridesTheAutomaticChoice()
    {
        var app = new MockElement { RuntimeId = [2], ControlType = "Group", AriaRole = "application" }.AddChild(Text(3, "Editor"));
        var document = Page(app);
        var modes = new Dictionary<string, InteractionMode>(StringComparer.OrdinalIgnoreCase) { ["code"] = InteractionMode.Browse };

        Assert.Equal(InteractionMode.Browse, DocumentModes.Initial(document, "Code", modes));
        Assert.Equal(InteractionMode.Focus, DocumentModes.Initial(document, "slack", modes));
    }

    [Fact]
    public void AppSetting_CanStartAnOrdinaryPageInFocusMode()
    {
        var document = Page(Text(2, "Chat"));
        var modes = new Dictionary<string, InteractionMode> { ["Discord"] = InteractionMode.Focus };

        Assert.Equal(InteractionMode.Focus, DocumentModes.Initial(document, "discord", modes));
    }
}
