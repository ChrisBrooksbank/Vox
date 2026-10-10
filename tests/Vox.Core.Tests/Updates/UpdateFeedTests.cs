using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Updates;
using Xunit;

namespace Vox.Core.Tests.Updates;

public sealed class UpdateFeedTests : IDisposable
{
    private readonly RSA _releaseKey = RSA.Create(2048);
    private readonly string _publicKey;

    public UpdateFeedTests() => _publicKey = _releaseKey.ExportSubjectPublicKeyInfoPem();

    public void Dispose() => _releaseKey.Dispose();

    private static readonly byte[] Installer = Encoding.UTF8.GetBytes("pretend msi");
    private static readonly string InstallerHash = Convert.ToHexString(SHA256.HashData(Installer));

    private static string Feed(string stable = "1.2.0", string beta = "1.3.0-beta", string? hash = null) =>
        $$"""
        { "releases": [
          { "version": "{{stable}}", "channel": "stable", "url": "https://example.com/Vox-{{stable}}.msi", "sha256": "{{hash ?? InstallerHash}}" },
          { "version": "{{beta.Replace("-beta", "")}}", "channel": "beta", "url": "https://example.com/Vox-beta.msi", "sha256": "{{hash ?? InstallerHash}}" }
        ] }
        """;

    private string Sign(string feed) =>
        Convert.ToBase64String(_releaseKey.SignData(Encoding.UTF8.GetBytes(feed), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

    [Fact]
    public void Parse_ReadsTheReleases()
    {
        var releases = UpdateFeed.Parse(Feed());

        Assert.Equal(["1.2.0", "1.3.0"], releases.Select(r => r.Version));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "other": [] }""")]
    [InlineData("""{ "releases": [ { "version": "x", "channel": "stable", "url": "https://e.com/a.msi", "sha256": "00" } ] }""")]
    [InlineData("""{ "releases": [ { "version": "1.0.0", "channel": "stable", "url": "http://e.com/a.msi", "sha256": "0000000000000000000000000000000000000000000000000000000000000000" } ] }""")]
    public void Parse_RejectsMalformedFeeds(string json) => Assert.Throws<FormatException>(() => UpdateFeed.Parse(json));

    [Fact]
    public void Newer_FollowsTheChannel()
    {
        var releases = UpdateFeed.Parse(Feed());

        Assert.Equal("1.2.0", UpdateFeed.Newer(releases, new Version(1, 1, 0), UpdateChannel.Stable)!.Version);
        Assert.Equal("1.3.0", UpdateFeed.Newer(releases, new Version(1, 1, 0), UpdateChannel.Beta)!.Version);
        Assert.Null(UpdateFeed.Newer(releases, new Version(1, 2, 0), UpdateChannel.Stable));
        Assert.Null(UpdateFeed.Newer(releases, new Version(1, 0, 0), UpdateChannel.Off));
    }

    [Fact]
    public void Signature_FromTheReleaseKey_IsAccepted()
    {
        var feed = Feed();

        Assert.True(UpdateFeed.IsSigned(Encoding.UTF8.GetBytes(feed), Sign(feed), _publicKey));
    }

    [Fact]
    public void Signature_OfAnAlteredFeed_OrFromAnotherKey_IsRejected()
    {
        var feed = Feed();
        var signature = Sign(feed);
        using var otherKey = RSA.Create(2048);

        Assert.False(UpdateFeed.IsSigned(Encoding.UTF8.GetBytes(feed.Replace("1.2.0", "9.9.9")), signature, _publicKey));
        Assert.False(UpdateFeed.IsSigned(Encoding.UTF8.GetBytes(feed), signature, otherKey.ExportSubjectPublicKeyInfoPem()));
        Assert.False(UpdateFeed.IsSigned(Encoding.UTF8.GetBytes(feed), "not base64!", _publicKey));
    }

    [Fact]
    public void Checksum_IsCheckedCaseInsensitively()
    {
        Assert.True(UpdateFeed.HasChecksum(new MemoryStream(Installer), InstallerHash.ToLowerInvariant()));
        Assert.False(UpdateFeed.HasChecksum(new MemoryStream(Encoding.UTF8.GetBytes("tampered")), InstallerHash));
    }

    private sealed class FakeWeb(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(files.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private UpdateChecker Checker(string feed, string signature, byte[]? installer = null, UpdateChannel channel = UpdateChannel.Stable) =>
        new(new HttpClient(new FakeWeb(new()
            {
                ["https://example.com/feed.json"] = Encoding.UTF8.GetBytes(feed),
                ["https://example.com/feed.json.sig"] = Encoding.UTF8.GetBytes(signature),
                ["https://example.com/Vox-1.2.0.msi"] = installer ?? Installer,
            })),
            () => channel, _publicKey, new Uri("https://example.com/feed.json"), new Version(1, 0, 0),
            NullLogger<UpdateChecker>.Instance);

    [Fact]
    public async Task Checker_FindsTheUpdate_InASignedFeed()
    {
        var feed = Feed();
        var checker = Checker(feed, Sign(feed));

        Assert.Equal(UpdateCheckResult.Available, await checker.CheckAsync());
        Assert.Equal("1.2.0", checker.Available!.Version);
    }

    [Fact]
    public async Task Checker_IgnoresAFeedWhoseSignatureDoesNotMatch()
    {
        var checker = Checker(Feed(stable: "9.0.0"), Sign(Feed()));

        Assert.Equal(UpdateCheckResult.Failed, await checker.CheckAsync());
        Assert.Null(checker.Available);
    }

    [Fact]
    public async Task Checker_WithoutAReleaseKey_IsOff()
    {
        var checker = new UpdateChecker(new HttpClient(), () => UpdateChannel.Stable, null, new Uri("https://example.com/feed.json"),
            new Version(1, 0), NullLogger<UpdateChecker>.Instance);

        Assert.False(checker.IsEnabled);
        Assert.Equal(UpdateCheckResult.Disabled, await checker.CheckAsync());
    }

    [Fact]
    public async Task Install_RunsOnlyAnInstallerWithTheSignedChecksum()
    {
        var feed = Feed();
        var good = Checker(feed, Sign(feed));
        await good.CheckAsync();
        string? started = null;
        Assert.True(await good.InstallAsync(good.Available!, path => started = path));
        Assert.True(File.Exists(started));
        File.Delete(started!);

        var tampered = Checker(feed, Sign(feed), installer: Encoding.UTF8.GetBytes("malware"));
        await tampered.CheckAsync();
        started = null;
        Assert.False(await tampered.InstallAsync(tampered.Available!, path => started = path));
        Assert.Null(started);
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "Vox-1.2.0.msi")));
    }
}
