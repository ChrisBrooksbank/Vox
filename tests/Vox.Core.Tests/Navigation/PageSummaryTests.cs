using Vox.Core.Buffer;
using Vox.Core.Navigation;
using Vox.Core.Tests.Buffer;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class PageSummaryTests
{
    private static VBufferDocument Page(string title = "Train times", string language = "en-GB")
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document", Name = title, Language = language };
        root.AddChild(new MockElement { RuntimeId = [2], Name = "Departures", AriaRole = "heading", HeadingLevel = 1 });
        root.AddChild(new MockElement { RuntimeId = [3], ControlType = "Group", AriaRole = "main" }
            .AddChild(new MockElement { RuntimeId = [4], Name = "Home", ControlType = "Hyperlink" })
            .AddChild(new MockElement { RuntimeId = [5], Name = "Help", ControlType = "Hyperlink" })
            .AddChild(new MockElement { RuntimeId = [6], Name = "From", ControlType = "Edit" }));
        return new VBufferBuilder().Build(root);
    }

    [Fact]
    public void Describe_SaysTitleLanguageAndCounts()
    {
        Assert.Equal("Train times, language English (United Kingdom), 1 heading, 2 links, 1 landmark, 1 form field, no tables",
            PageSummary.Describe(Page()));
    }

    [Fact]
    public void Describe_UntitledPageWithoutLanguage()
    {
        Assert.StartsWith("Untitled page, 1 heading", PageSummary.Describe(Page(title: "", language: "")));
    }

    [Fact]
    public void LinkUrl_IsTheLinksValue()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        root.AddChild(new LinkElement { RuntimeId = [2], Name = "Docs", Value = "https://example.com/docs" });
        root.AddChild(new MockElement { RuntimeId = [3], Name = "Plain text" });
        root.AddChild(new LinkElement { RuntimeId = [4], Name = "Empty" });
        var document = new VBufferBuilder().Build(root);

        Assert.Equal("https://example.com/docs", PageSummary.LinkUrlText(document.FindByRuntimeId([2])));
        Assert.Equal("Not on a link", PageSummary.LinkUrlText(document.FindByRuntimeId([3])));
        Assert.Equal("Link has no address", PageSummary.LinkUrlText(document.FindByRuntimeId([4])));
    }

    private sealed class LinkElement : IVBufferElement
    {
        public int[] RuntimeId { get; init; } = [];
        public string Name { get; init; } = string.Empty;
        public string ControlType => "Hyperlink";
        public string AriaRole => string.Empty;
        public string AriaProperties => string.Empty;
        public bool IsFocusable => true;
        public string Value { get; init; } = string.Empty;
        public IReadOnlyList<IVBufferElement> GetChildren() => [];
    }
}
