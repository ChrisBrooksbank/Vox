using Vox.Core.Labels;
using Xunit;

namespace Vox.Core.Tests.Labels;

public sealed class LabelPackTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VoxLabelPacks_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    private static readonly CustomLabel Send = new("https://mail.example.com", "id:send", "Send");
    private static readonly CustomLabel Logo = new("https://news.example.org", "Image|banner>Document|", "Home", PathPrefix: "/world", Described: true);

    [Fact]
    public void APack_WrittenAndRead_HasTheSameLabels()
    {
        var path = Path.Combine(_directory, "mail" + LabelPack.Extension);

        LabelPack.Write(path, [Send, Logo]);

        Assert.Equal([Send, Logo], LabelPack.Read(path).OrderBy(l => l.Site));
        Assert.Equal(["https://mail.example.com", "https://news.example.org"], LabelPack.SitesOf([Logo, Send, Send]));
    }

    [Fact]
    public void AFileThatIsNoPack_IsRefused()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "bad" + LabelPack.Extension);
        File.WriteAllText(path, "[ not a pack");

        Assert.Throws<FormatException>(() => LabelPack.Read(path));
        Assert.Empty(LabelPack.Parse("{ }"));
    }

    [Fact]
    public void APacksLabels_Apply_UnderTheUsersOwn_AndAreNotKeptInTheirFile()
    {
        var file = Path.Combine(_directory, CustomLabelStore.FileName);
        var store = new CustomLabelStore(file);
        int changes = 0;
        store.Changed += (_, _) => changes++;

        store.SetPack("com.example.mail-labels", [Send, Send with { Signature = "id:archive", Label = "Archive" }]);
        Assert.Equal("Send", store.LabelsFor("https://mail.example.com/inbox")!.Get("id:send"));
        Assert.Equal("Archive", store.LabelsFor("https://mail.example.com/inbox")!.Get("id:archive"));
        Assert.Empty(store.All);

        store.Set(Send with { Label = "Send now" });
        Assert.Equal("Send now", store.LabelsFor("https://mail.example.com/inbox")!.Get("id:send"));
        Assert.Equal([Send with { Label = "Send now" }], new CustomLabelStore(file).All);

        store.RemovePack("com.example.mail-labels");
        store.RemovePack("com.example.mail-labels");
        Assert.Null(store.LabelsFor("https://mail.example.com/inbox")!.Get("id:archive"));
        Assert.Equal(3, changes);

        // No packs on secure screens either
        var secure = new CustomLabelStore(null);
        secure.SetPack("com.example.mail-labels", [Send]);
        Assert.Null(secure.LabelsFor("https://mail.example.com"));
    }

    [Fact]
    public void Editor_ImportsAPack_ReplacingLabelsForTheSameElements_AndExportsASite()
    {
        var editor = new CustomLabelsEditor([Send with { Label = "Old" }, Logo], currentSite: "https://mail.example.com");

        Assert.Equal(2, editor.Merge([Send, Send with { Signature = "id:archive", Label = "Archive" }]));

        Assert.True(editor.Changed);
        Assert.Equal(3, editor.Labels.Count);
        Assert.DoesNotContain(editor.Labels, l => l.Label == "Old");
        Assert.Equal("https://news.example.org", editor.ExportSite(editor.Labels.ToList().IndexOf(Logo)));
        Assert.Equal("https://mail.example.com", editor.ExportSite(-1));
        Assert.Equal(["Archive", "Send"], editor.LabelsOf("https://MAIL.example.com").Select(l => l.Label).Order());
        Assert.Equal(0, new CustomLabelsEditor([]).Merge([]));
        Assert.Null(new CustomLabelsEditor([]).ExportSite(-1));
    }
}
