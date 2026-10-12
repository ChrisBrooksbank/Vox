using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.AddOns;
using Vox.Core.Enterprise;
using Vox.Core.Lifecycle;
using Xunit;

namespace Vox.Core.Tests.AddOns;

public class AddOnStoreTests : IDisposable
{
    private readonly AddOnTestFiles _files = new();
    private readonly RSA _key = RSA.Create(2048);
    private static readonly Uri Catalogue = new("https://addons.example.com/catalogue.json");

    public void Dispose()
    {
        _files.Dispose();
        _key.Dispose();
    }

    private static string Entry(string id, string version, string url, string sha, string min = "1.0", string name = "Add-on") =>
        $$"""{ "id": "{{id}}", "name": "{{name}} {{id}}", "version": "{{version}}", "author": "Example", "minApiVersion": "{{min}}", "url": "{{url}}", "sha256": "{{sha}}", "description": "Reads {{id}} tables" }""";

    private static string CatalogueJson(params string[] entries) => $$"""{ "addons": [ {{string.Join(",", entries)}} ] }""";

    private string Sign(string json) =>
        Convert.ToBase64String(_key.SignData(Encoding.UTF8.GetBytes(json), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

    /// <summary>Serves the catalogue, its signature and packages from memory.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(Files.TryGetValue(request.RequestUri!.AbsoluteUri, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private AddOnStore Store(FakeServer server, RunPolicy? policy = null, Uri? catalogue = null, AddOnTrustStore? trust = null) =>
        new(NetworkGate.CreateHttpClient(policy ?? RunPolicy.Normal, TimeSpan.FromSeconds(5), server), () => catalogue ?? Catalogue,
            trust ?? AddOnTestFiles.Trusting(_key), policy ?? RunPolicy.Normal, NullLogger<AddOnStore>.Instance);

    [Fact]
    public async Task ASignedCatalogue_IsRead_WithAddressesRelativeToIt()
    {
        var json = CatalogueJson(Entry("com.example.a", "1.0.0", "packages/a.voxaddon", new string('a', 64)));
        var server = new FakeServer();
        server.Files[Catalogue.AbsoluteUri] = Encoding.UTF8.GetBytes(json);
        server.Files[Catalogue.AbsoluteUri + ".sig"] = Encoding.ASCII.GetBytes(Sign(json));

        var entries = await Store(server).FetchAsync();

        var entry = Assert.Single(entries);
        Assert.Equal("https://addons.example.com/packages/a.voxaddon", entry.Url);
    }

    [Fact]
    public async Task ACatalogueSignedByAnotherKey_IsRefused()
    {
        var json = CatalogueJson(Entry("com.example.a", "1.0.0", "a.voxaddon", new string('a', 64)));
        using var other = RSA.Create(2048);
        var server = new FakeServer();
        server.Files[Catalogue.AbsoluteUri] = Encoding.UTF8.GetBytes(json);
        server.Files[Catalogue.AbsoluteUri + ".sig"] = Encoding.ASCII.GetBytes(
            Convert.ToBase64String(other.SignData(Encoding.UTF8.GetBytes(json), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));

        await Assert.ThrowsAsync<AddOnStoreException>(() => Store(server).FetchAsync());
    }

    [Fact]
    public async Task WithTheNetworkOff_TheStoreAsksNothing()
    {
        var server = new FakeServer();
        var store = Store(server, RunPolicy.Normal with { NetworkOffByPolicy = true });

        Assert.Equal(StoreAvailability.NetworkOff, store.Availability);
        await Assert.ThrowsAsync<AddOnStoreException>(() => store.FetchAsync());
        Assert.Equal(0, server.Requests);
    }

    [Fact]
    public void WithoutAKey_OrWithAddOnsOff_ThereIsNoStore()
    {
        Assert.Equal(StoreAvailability.NoKey, Store(new FakeServer(), trust: AddOnTrustStore.Empty).Availability);
        Assert.Equal(StoreAvailability.AddOnsOff, Store(new FakeServer(), RunPolicy.Secure).Availability);
    }

    [Fact]
    public void RemoteEntries_MustBeHttps()
    {
        var json = CatalogueJson(Entry("com.example.a", "1.0.0", "http://example.com/a.voxaddon", new string('a', 64)));

        Assert.Throws<FormatException>(() => AddOnCatalogue.Parse(json, Catalogue));
    }

    [Fact]
    public void BadEntries_AreRefused()
    {
        Assert.Throws<FormatException>(() => AddOnCatalogue.Parse(CatalogueJson(Entry("BAD", "1.0.0", "a.voxaddon", new string('a', 64))), Catalogue));
        Assert.Throws<FormatException>(() => AddOnCatalogue.Parse(CatalogueJson(Entry("com.example.a", "1.0.0", "a.voxaddon", "short")), Catalogue));
        Assert.Throws<FormatException>(() => AddOnCatalogue.Parse("{}", Catalogue));
    }

    [Fact]
    public void Browse_PicksTheNewestCompatibleVersion_AndSaysWhatIsInstalled()
    {
        var entries = AddOnCatalogue.Parse(CatalogueJson(
            Entry("com.example.a", "1.0.0", "a1.voxaddon", new string('a', 64)),
            Entry("com.example.a", "1.5.0", "a15.voxaddon", new string('b', 64)),
            Entry("com.example.a", "2.0.0", "a2.voxaddon", new string('c', 64), min: "9.0"),
            Entry("com.example.b", "1.0.0", "b.voxaddon", new string('d', 64), min: "9.0"),
            Entry("com.example.c", "1.0.0", "c.voxaddon", new string('e', 64))), Catalogue);
        var installed = new[]
        {
            new AddOnInfo(AddOnManifestTests.Valid("com.example.a") with { Version = "1.0.0" }, AddOnState.Running, null, AddOnTrust.Catalogue, null, []),
            new AddOnInfo(AddOnManifestTests.Valid("com.example.c") with { Version = "1.0.0" }, AddOnState.Running, null, AddOnTrust.Catalogue, null, []),
        };

        var items = AddOnCatalogue.Browse(entries, installed, new Version(1, 0)).ToDictionary(i => i.Entry.Id);

        Assert.Equal("1.5.0", items["com.example.a"].Entry.Version);
        Assert.True(items["com.example.a"].UpdateAvailable);
        Assert.NotNull(items["com.example.b"].Incompatible);
        Assert.False(items["com.example.b"].UpdateAvailable);
        Assert.False(items["com.example.c"].UpdateAvailable);
        Assert.Equal("1.0.0", items["com.example.c"].Installed);
    }

    [Fact]
    public void Search_MatchesEveryWord_InNameDescriptionOrAuthor()
    {
        var entries = AddOnCatalogue.Parse(CatalogueJson(
            Entry("com.example.excel", "1.0.0", "a.voxaddon", new string('a', 64), name: "Spreadsheet"),
            Entry("com.example.word", "1.0.0", "b.voxaddon", new string('b', 64), name: "Documents")), Catalogue);
        var items = AddOnCatalogue.Browse(entries, [], new Version(1, 0));

        Assert.Equal("com.example.excel", Assert.Single(AddOnCatalogue.Search(items, "spreadsheet excel")).Entry.Id);
        Assert.Equal(2, AddOnCatalogue.Search(items, "example").Count);
        Assert.Equal(2, AddOnCatalogue.Search(items, "  ").Count);
        Assert.Empty(AddOnCatalogue.Search(items, "braille"));
    }

    [Fact]
    public async Task ACatalogueOnAnotherComputer_IsTheNetwork()
    {
        var unc = new Uri(@"\\files.example.com\share\catalogue.json");
        var networkOff = RunPolicy.Normal with { NetworkOffByPolicy = true };

        Assert.True(unc.IsFile && unc.IsUnc);
        Assert.Equal(StoreAvailability.NetworkOff, Store(new FakeServer(), networkOff, unc).Availability);
        await Assert.ThrowsAsync<AddOnStoreException>(() => Store(new FakeServer(), networkOff, unc).FetchAsync());
        Assert.Equal(StoreAvailability.Available, Store(new FakeServer(), RunPolicy.Normal, unc).Availability);
    }

    [Fact]
    public async Task APackageOnAnotherComputer_IsNotCopied_WhileTheNetworkIsOff()
    {
        var store = Store(new FakeServer(), RunPolicy.Normal with { NetworkOffByPolicy = true }, new Uri(_files.PathOf("catalogue.json")));
        var entry = new CatalogueEntry("com.example.a", "Example", "1.0.0", "Example", "1.0", "file://files.example.com/share/a.voxaddon", new string('a', 64));

        await Assert.ThrowsAsync<AddOnStoreException>(() => store.DownloadAsync(entry));
    }

    [Fact]
    public async Task ALocalCatalogue_ListsPackagesBesideIt_AndOneInstallsThroughTheManager()
    {
        var package = _files.Package();
        var sha = AddOnPackage.Read(package, AddOnTrustStore.Empty).Sha256;
        var json = CatalogueJson(Entry("com.example.test", "1.0.0", Path.GetFileName(package), sha));
        var cataloguePath = _files.PathOf("catalogue.json");
        File.WriteAllText(cataloguePath, json);
        File.WriteAllText(cataloguePath + ".sig", Sign(json));
        var server = new FakeServer();
        // A catalogue on this computer works even with the network off
        var policy = RunPolicy.Normal with { NetworkOffByPolicy = true };
        var store = Store(server, policy, new Uri(cataloguePath));
        var loader = new FakeLoader();
        var manager = new AddOnManager(_files.AddOnsDirectory, policy, loader, AddOnTrustStore.Empty);

        var entry = Assert.Single(await store.FetchAsync());
        var downloaded = await store.DownloadAsync(entry);
        var check = manager.CheckPackage(downloaded, entry.Sha256);
        manager.InstallFiles(check);
        AddOnStore.DeleteDownload(downloaded);
        manager.LoadAll();

        Assert.Equal(AddOnTrust.Catalogue, check.Package.Trust);
        Assert.Equal(AddOnState.Running, Assert.Single(manager.List()).State);
        Assert.False(File.Exists(downloaded));
        Assert.Equal(0, server.Requests);
    }

    [Fact]
    public async Task ADownloadThatIsntTheCataloguesPackage_IsRefused()
    {
        var url = "https://addons.example.com/a.voxaddon";
        var server = new FakeServer();
        server.Files[url] = File.ReadAllBytes(_files.Package());
        var entry = new CatalogueEntry("com.example.test", "Test", "1.0.0", "Example", "1.0", url, new string('0', 64));
        var manager = new AddOnManager(_files.AddOnsDirectory, RunPolicy.Normal, new FakeLoader(), AddOnTrustStore.Empty);

        var path = await Store(server).DownloadAsync(entry);

        Assert.Throws<AddOnPackageException>(() => manager.CheckPackage(path, entry.Sha256));
        AddOnStore.DeleteDownload(path);
    }
}
