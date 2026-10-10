using Vox.Core.Buffer;
using Xunit;

namespace Vox.Core.Tests.Buffer;

public class ScreenLayoutTests
{
    // <p>Read <a>the guide</a> first.</p>
    private static MockElement Page()
    {
        var paragraph = new MockElement { RuntimeId = [2], ControlType = "Group" }
            .AddChild(new MockElement { RuntimeId = [3], Name = "Read " })
            .AddChild(new MockElement { RuntimeId = [4], Name = "the guide", ControlType = "Hyperlink" })
            .AddChild(new MockElement { RuntimeId = [5], Name = " first." });
        return new MockElement { RuntimeId = [1], ControlType = "Document" }.AddChild(paragraph);
    }

    [Fact]
    public void ScreenLayout_KeepsInlineTextOnOneLine()
    {
        var document = new VBufferBuilder().Build(Page());

        Assert.True(document.ScreenLayout);
        Assert.Equal("Read  the guide  first.\n", document.FlatText);
    }

    [Fact]
    public void WithoutScreenLayout_EachElementIsOnItsOwnLine()
    {
        var document = new VBufferBuilder { ScreenLayout = false }.Build(Page());

        Assert.False(document.ScreenLayout);
        Assert.Equal("Read \nthe guide\n first.\n", document.FlatText);
    }

    [Fact]
    public void Update_KeepsTheDocumentsLayout()
    {
        var document = new VBufferBuilder { ScreenLayout = false }.Build(Page());

        var updated = new IncrementalUpdater().ApplyUpdate(document, [4],
            new MockElement { RuntimeId = [4], Name = "the manual", ControlType = "Hyperlink" });

        Assert.False(updated.ScreenLayout);
        Assert.Equal("Read \nthe manual\n first.\n", updated.FlatText);
    }
}
