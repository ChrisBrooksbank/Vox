using System.Text.Json;
using Vox.Core.AddOns;
using Vox.Core.Labels;
using Vox.Core.Lifecycle;
using Xunit;

namespace Vox.Core.Tests.AddOns;

/// <summary>Label packs published as add-ons (type "labels"), installed and taken back like any other.</summary>
public class LabelPackAddOnTests : IDisposable
{
    private const string Id = "com.example.mail-labels";
    private readonly AddOnTestFiles _files = new();
    private readonly FakeLoader _loader = new();
    private readonly Dictionary<string, IReadOnlyList<CustomLabel>> _packs = new();

    public void Dispose() => _files.Dispose();

    private static readonly CustomLabel Send = new("https://mail.example.com", "id:send", "Send");

    private AddOnManager Manager(RunPolicy? policy = null) =>
        new(_files.AddOnsDirectory, policy ?? RunPolicy.Normal, _loader, AddOnTrustStore.Empty, new AddOnIntegration
        {
            SetLabelPack = (id, labels) =>
            {
                if (labels is null)
                    _packs.Remove(id);
                else
                    _packs[id] = labels;
            },
        });

    /// <summary>A label pack's source folder: addon.json of type "labels" and labels.json.</summary>
    private string Source(string version = "1.0.0", string? labels = null)
    {
        var folder = _files.PathOf("pack-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, LabelPack.FileName), labels ?? LabelPack.ToJson([Send]));
        File.WriteAllText(Path.Combine(folder, AddOnManifest.FileName), JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = Id, ["name"] = "Mail labels", ["version"] = version, ["author"] = "Example",
            ["minApiVersion"] = "1.0", ["type"] = AddOnManifest.LabelPackType,
        }));
        return folder;
    }

    [Fact]
    public void Manifest_ALabelPack_NeedsItsLabelsFile_NotAnAssembly()
    {
        var hash = new string('a', 64);
        var pack = new AddOnManifest
        {
            Id = Id, Name = "Mail labels", Version = "1.0.0", Author = "Example", MinApiVersion = "1.0",
            Type = AddOnManifest.LabelPackType, Files = new() { [LabelPack.FileName] = hash },
        };

        Assert.Empty(pack.Problems());
        Assert.True(pack.IsLabelPack);
        Assert.Contains((pack with { Files = new() { ["other.json"] = hash } }).Problems(), p => p.Contains(LabelPack.FileName));
        Assert.Contains((pack with { Type = "theme" }).Problems(), p => p.Contains("type"));
        // A code add-on still needs its assembly
        Assert.Contains((pack with { Type = null }).Problems(), p => p.Contains("entryAssembly"));
    }

    [Fact]
    public void AnInstalledLabelPack_GivesItsLabels_WithoutLoadingCode_AndDisablingTakesThemBack()
    {
        var manager = Manager();
        var check = manager.CheckPackage(_files.Package(Source()));
        Assert.True(check.Manifest.IsLabelPack);
        manager.InstallFiles(check);

        manager.LoadAll();

        Assert.Equal([Send], _packs[Id]);
        Assert.Empty(_loader.Loaded);
        var info = Assert.Single(manager.List());
        Assert.Equal(AddOnState.Running, info.State);
        Assert.Contains("Label pack", AddOnCommands.Describe(info));

        Assert.True(manager.Disable(Id));
        Assert.False(_packs.ContainsKey(Id));
        Assert.True(manager.Enable(Id));
        Assert.True(_packs.ContainsKey(Id));
        Assert.True(manager.Remove(Id));
        Assert.Empty(_packs);
    }

    [Fact]
    public void AnUpdate_ReplacesThePacksLabels()
    {
        var manager = Manager();
        manager.InstallFiles(manager.CheckPackage(_files.Package(Source())));
        manager.LoadAll();

        var check = manager.CheckPackage(_files.Package(Source("1.1.0", LabelPack.ToJson([Send with { Label = "Send mail" }]))));
        Assert.Equal("1.0.0", check.Installed);
        manager.InstallFiles(check);
        manager.Reload(Id);

        Assert.Equal("Send mail", Assert.Single(_packs[Id]).Label);
    }

    [Fact]
    public void ALabelPackChangedAfterInstalling_IsNotUsed()
    {
        var manager = Manager();
        manager.InstallFiles(manager.CheckPackage(_files.Package(Source())));
        File.WriteAllText(Path.Combine(_files.AddOnsDirectory, Id, LabelPack.FileName), LabelPack.ToJson([Send with { Label = "Evil" }]));

        manager.LoadAll();

        Assert.Empty(_packs);
        Assert.Equal(AddOnState.Failed, Assert.Single(manager.List()).State);
    }

    [Fact]
    public void LabelPacks_AreNotUsedInSafeMode()
    {
        var manager = Manager();
        manager.InstallFiles(manager.CheckPackage(_files.Package(Source())));

        Manager(RunPolicy.Normal with { SafeMode = true }).LoadAll();

        Assert.Empty(_packs);
    }

    [Fact]
    public void TheCatalogue_ListsLabelPacks()
    {
        var json = $$"""
            { "addons": [ { "id": "{{Id}}", "name": "Mail labels", "version": "1.0.0", "author": "Example", "minApiVersion": "1.0",
              "url": "mail-labels.voxaddon", "sha256": "{{new string('b', 64)}}", "type": "labels" } ] }
            """;

        var entry = Assert.Single(AddOnCatalogue.Parse(json, new Uri("https://addons.example.com/catalogue.json")));

        Assert.True(entry.IsLabelPack);
        Assert.Equal("https://addons.example.com/mail-labels.voxaddon", entry.Url);
        Assert.Throws<FormatException>(() => AddOnCatalogue.Parse(json.Replace("\"labels\"", "\"theme\""), new Uri("https://addons.example.com/catalogue.json")));
    }
}
