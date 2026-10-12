using Vox.Core.Buffer;
using Vox.Core.Navigation;
using Vox.Core.Tests.Buffer;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class MainContentTests
{
    private static int _id = 8000;

    private static MockElement Text(string name) => new() { RuntimeId = [_id++], Name = name };
    private static MockElement Link(string name) => new() { RuntimeId = [_id++], Name = name, ControlType = "Hyperlink" };
    private static MockElement Heading(string name, int level) =>
        new() { RuntimeId = [_id++], Name = name, AriaRole = "heading", AriaProperties = $"level={level}" };

    private static MockElement Paragraph(string text) =>
        new MockElement { RuntimeId = [_id++], ControlType = "Group" }.AddChild(Text(text));

    private static MockElement Region(string role, params MockElement[] children)
    {
        var region = new MockElement { RuntimeId = [_id++], ControlType = "Group", AriaRole = role };
        foreach (var child in children)
            region.AddChild(child);
        return region;
    }

    private static VBufferDocument Page(params MockElement[] children)
    {
        var root = new MockElement { RuntimeId = [_id++], ControlType = "Document", Name = "News" };
        foreach (var child in children)
            root.AddChild(child);
        return new VBufferBuilder().Build(root);
    }

    private const string Prose =
        "The council approved the new park on Tuesday after a long debate about its cost and its location.";

    [Fact]
    public void TheMainLandmark_ComesFirst_ThenTheHeading()
    {
        var document = Page(Region("navigation", Link("Home"), Link("Sport")),
            Region("main", Heading("Park approved", 1), Text(Prose)));

        var suggestions = MainContent.Suggestions(document);

        Assert.Equal("Main landmark", suggestions[0].Reason);
        Assert.Equal("Main", suggestions[0].Node.LandmarkType);
        // The heading starts where the landmark does: not suggested twice
        Assert.DoesNotContain(suggestions.Skip(1), s => s.Reason == "Main heading");
    }

    [Fact]
    public void WithoutLandmarks_TheFirstLevel1Heading()
    {
        var document = Page(Link("Home"), Link("Sport"), Text("Menu"), Heading("Park approved", 1), Text(Prose));

        var best = MainContent.Best(document)!;

        Assert.Equal("Main heading", best.Reason);
        Assert.Equal("Park approved", best.Node.Name);
    }

    [Fact]
    public void WithoutEither_TheLongestPassageOfProse()
    {
        // Paragraphs: each on its own line
        var document = Page(Region("navigation", Paragraph(Prose + " In the menu.")), Paragraph("Home link list"),
            Paragraph("Short line"), Paragraph(Prose), Paragraph(Prose + " More."), Paragraph("Footer"));

        var best = MainContent.Best(document)!;

        Assert.Equal("Longest passage of text", best.Reason);
        Assert.Equal(Prose, best.Node.Name);
    }

    [Fact]
    public void ALinksOnlyPage_HasNoSuggestion()
    {
        Assert.Empty(MainContent.Suggestions(Page(Link("Home"), Link("Sport"))));
    }
}

public class PageDigestTests
{
    [Fact]
    public void TheSummary_SaysWhatThePageIsAndHolds()
    {
        int id = 8500;
        var root = new MockElement { RuntimeId = [id++], ControlType = "Document", Name = "City news" };
        var main = new MockElement { RuntimeId = [id++], ControlType = "Group", AriaRole = "main" };
        main.AddChild(new MockElement { RuntimeId = [id++], Name = "Park approved", AriaRole = "heading", AriaProperties = "level=1" });
        main.AddChild(new MockElement { RuntimeId = [id++], Name = "The council approved the new park on Tuesday after a long debate. Work starts in May." });
        main.AddChild(new MockElement { RuntimeId = [id++], Name = "Cost", AriaRole = "heading", AriaProperties = "level=2" });
        main.AddChild(new MockElement { RuntimeId = [id++], Name = "Location", AriaRole = "heading", AriaProperties = "level=2" });
        root.AddChild(main);
        var document = new VBufferBuilder().Build(root);

        var lines = PageDigest.Summarize(document, "Insert+Ctrl+M").Split('\n');

        Assert.Equal(
        [
            "City news",
            "Main heading: Park approved",
            "About a minute to read",
            "Sections: Cost, Location",
            "It opens: The council approved the new park on Tuesday after a long debate.",
            "Insert+Ctrl+M moves to the main content.",
        ], lines);
    }

    [Fact]
    public void ManySections_AreCounted()
    {
        int id = 8600;
        var root = new MockElement { RuntimeId = [id++], ControlType = "Document", Name = "Docs" };
        for (int i = 1; i <= 8; i++)
            root.AddChild(new MockElement { RuntimeId = [id++], Name = $"Part {i}", AriaRole = "heading", AriaProperties = "level=2" });

        var summary = PageDigest.Summarize(new VBufferBuilder().Build(root));

        Assert.Contains("Sections: Part 1, Part 2, Part 3, Part 4, Part 5, Part 6, and 2 more", summary);
    }
}
