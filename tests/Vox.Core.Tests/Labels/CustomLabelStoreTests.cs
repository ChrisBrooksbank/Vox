using Vox.Core.Labels;
using Xunit;

namespace Vox.Core.Tests.Labels;

public sealed class CustomLabelStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VoxLabels_" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, CustomLabelStore.FileName);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("https://Example.com/a/b?c=1", "https://example.com")]
    [InlineData("http://example.com:8080/", "http://example.com:8080")]
    [InlineData("file:///C:/page.html", null)]
    [InlineData("about:blank", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void SiteOf_IsSchemeAndHost(string? address, string? expected) =>
        Assert.Equal(expected, CustomLabelSites.SiteOf(address));

    [Fact]
    public void LabelsFor_TheSite_ALongerPathPrefixWinning()
    {
        var store = new CustomLabelStore(FilePath);
        store.Set(new CustomLabel("https://example.com", "id:send", "Send"));
        store.Set(new CustomLabel("https://example.com", "id:send", "Send mail", PathPrefix: "/mail"));
        store.Set(new CustomLabel("https://other.org", "id:send", "Other"));

        Assert.Equal("Send", store.LabelsFor("https://example.com/news")!.Get("id:send"));
        Assert.Equal("Send mail", store.LabelsFor("https://example.com/mail/inbox")!.Get("id:send"));
        Assert.Equal("Other", store.LabelsFor("https://other.org")!.Get("id:send"));
        Assert.Null(store.LabelsFor("https://nothing.net"));
        Assert.Null(store.LabelsFor(null));
    }

    [Fact]
    public void Set_ReplacesTheSameElementsLabel_AndIsKeptInTheFile()
    {
        var store = new CustomLabelStore(FilePath);
        int changes = 0;
        store.Changed += (_, _) => changes++;

        Assert.True(store.Set(new CustomLabel("https://example.com", "button||search", "Go")));
        Assert.True(store.Set(new CustomLabel("https://example.com", "button||search", "Search")));

        Assert.Equal(2, changes);
        var reloaded = new CustomLabelStore(FilePath);
        var label = Assert.Single(reloaded.All);
        Assert.Equal("Search", label.Label);
        Assert.Equal(label, reloaded.Find("https://example.com", "button||search"));
    }

    [Fact]
    public void Remove_AndReplaceAll()
    {
        var store = new CustomLabelStore(FilePath);
        var a = new CustomLabel("https://example.com", "id:a", "A");
        var b = new CustomLabel("https://example.com", "id:b", "B", Described: true);
        store.SetAll([a, b]);

        store.Remove(a);
        Assert.Equal([b], store.All);

        store.ReplaceAll([a with { Label = "New A" }]);
        Assert.Equal("New A", Assert.Single(new CustomLabelStore(FilePath).All).Label);
    }

    [Fact]
    public void WithoutAFile_ThereAreNoLabels_AndNoneCanBeAdded()
    {
        var store = new CustomLabelStore(null);

        Assert.False(store.IsAvailable);
        Assert.False(store.Set(new CustomLabel("https://example.com", "id:a", "A")));
        Assert.Empty(store.All);
        Assert.Null(store.LabelsFor("https://example.com"));
    }

    [Fact]
    public void Parse_LeavesOutBadSitesAndEntries_AndAnUnreadableFileGivesNone()
    {
        var labels = CustomLabelStore.Parse("""
            { "sites": {
                "https://example.com": [
                  { "signature": "id:a", "label": "A", "described": true },
                  { "signature": "", "label": "No signature" },
                  { "signature": "id:c", "label": "  " },
                  null
                ],
                "not a site": [ { "signature": "id:x", "label": "X" } ]
            } }
            """);
        Assert.Equal([new CustomLabel("https://example.com", "id:a", "A", Described: true)], labels);

        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{ not json");
        Assert.Empty(new CustomLabelStore(FilePath).All);
    }

    [Fact]
    public void Editor_ListsTheCurrentSiteFirst_RenamesAndDeletes()
    {
        var editor = new CustomLabelsEditor(
        [
            new CustomLabel("https://b.org", "id:1", "Zeta"),
            new CustomLabel("https://a.com", "id:2", "Beta", Described: true),
            new CustomLabel("https://a.com", "id:3", "Alpha", PathPrefix: "/mail"),
        ], currentSite: "https://b.org");

        Assert.Equal(["Zeta", "Alpha", "Beta"], editor.Labels.Select(l => l.Label));
        Assert.Equal("Alpha, a.com/mail", editor.Describe(1));
        Assert.Equal("Beta, a.com (described)", editor.Describe(2));
        Assert.False(editor.Changed);

        Assert.False(editor.Rename(2, "  "));
        Assert.True(editor.Rename(2, "Bold"));
        Assert.Equal(new CustomLabel("https://a.com", "id:2", "Bold"), editor.Labels[2]);
        Assert.True(editor.Delete(0));
        Assert.False(editor.Delete(5));
        Assert.True(editor.Changed);
        Assert.Equal(["Alpha", "Bold"], editor.Labels.Select(l => l.Label));
    }
}
