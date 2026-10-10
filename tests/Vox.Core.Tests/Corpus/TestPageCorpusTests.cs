using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Vox.Core.Tests.Corpus;

/// <summary>The test-page corpus (tests/pages) and its manifest stay in step, and every page is self-contained.</summary>
public class TestPageCorpusTests
{
    private static readonly string PagesDirectory =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "pages"));

    private static IReadOnlyList<(string File, string Title)> Manifest()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(PagesDirectory, "manifest.json")));
        return json.RootElement.GetProperty("pages").EnumerateArray()
            .Select(p => (p.GetProperty("file").GetString()!, p.GetProperty("title").GetString()!))
            .ToList();
    }

    [Fact]
    public void EveryPageIsInTheManifest_AndEveryEntryHasAPage()
    {
        var listed = Manifest().Select(p => p.File).ToHashSet();
        var pages = Directory.GetFiles(PagesDirectory, "*.html").Select(Path.GetFileName)
            .Where(f => f != "index.html").ToHashSet();

        Assert.Equal(pages.Order(), listed.Order());
    }

    [Fact]
    public void EveryPageHasItsTitleAndLanguage_AndIsLinkedFromTheIndex()
    {
        var index = File.ReadAllText(Path.Combine(PagesDirectory, "index.html"));
        foreach (var (file, title) in Manifest())
        {
            var html = File.ReadAllText(Path.Combine(PagesDirectory, file));
            Assert.Contains($"<title>{title}</title>", html);
            Assert.Contains("<html lang=\"en\">", html);
            Assert.Contains($"href=\"{file}\"", index);
        }
    }

    [Fact]
    public void PagesUseNoNetwork()
    {
        foreach (var file in Directory.GetFiles(PagesDirectory, "*.html"))
        {
            var html = File.ReadAllText(file);
            Assert.False(Regex.IsMatch(html, @"(src|href)\s*=\s*""(https?:)?//"), $"{Path.GetFileName(file)} loads something from the network");
        }
    }
}
