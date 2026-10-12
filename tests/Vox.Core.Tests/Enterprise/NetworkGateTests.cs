using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Braille.Brltty;
using Vox.Core.Enterprise;
using Vox.Core.Lifecycle;
using Vox.Core.Tests.TestSupport;
using Vox.Core.Updates;
using Xunit;

namespace Vox.Core.Tests.Enterprise;

/// <summary>
/// The network-off guarantee: with <see cref="RunPolicy.AllowNetwork"/> false (DisableNetwork
/// policy, secure screens) nothing leaves the computer. Every piece of code that can open a
/// connection is listed here and its gate tested; a new one fails <see cref="EveryNetworkUser_IsKnownAndGated"/>.
/// </summary>
public class NetworkGateTests
{
    private static readonly RunPolicy NetworkOff = RunPolicy.Normal with { NetworkOffByPolicy = true };

    /// <summary>Fails the test if anything is sent.</summary>
    private sealed class Tripwire : HttpMessageHandler
    {
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    [Fact]
    public async Task TheHttpClient_SendsNothing_WhileTheNetworkIsOff()
    {
        var wire = new Tripwire();
        using var http = NetworkGate.CreateHttpClient(NetworkOff, TimeSpan.FromSeconds(5), wire);

        var refused = await Assert.ThrowsAsync<NetworkDisabledException>(() => http.GetStringAsync("https://example.com/feed.json"));

        Assert.Contains("example.com", refused.Message);
        Assert.Equal(0, wire.Requests);
    }

    [Fact]
    public async Task TheHttpClient_Sends_WhileTheNetworkIsAllowed()
    {
        var wire = new Tripwire();
        using var http = NetworkGate.CreateHttpClient(RunPolicy.Normal, TimeSpan.FromSeconds(5), wire);

        await http.GetStringAsync("https://example.com/feed.json");

        Assert.Equal(1, wire.Requests);
    }

    [Fact]
    public async Task TheSecureScreens_SendNothingEither()
    {
        var wire = new Tripwire();
        using var http = NetworkGate.CreateHttpClient(RunPolicy.Secure, TimeSpan.FromSeconds(5), wire);

        await Assert.ThrowsAsync<NetworkDisabledException>(() => http.GetStringAsync("https://example.com/"));
        Assert.Equal(0, wire.Requests);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.0.0.53", true)]
    [InlineData("::1", true)]
    [InlineData("[::1]", true)]
    [InlineData("LOCALHOST", true)]
    [InlineData("192.0.2.1", false)]
    [InlineData("braille.example.com", false)]
    public void OnlyThisComputer_IsReachable_WhileTheNetworkIsOff(string host, bool reachable)
    {
        Assert.Equal(reachable, NetworkGate.MayConnect(NetworkOff, host));
        Assert.True(NetworkGate.MayConnect(RunPolicy.Normal, host));
    }

    [Fact]
    public async Task TheUpdateChecker_NeverChecks_WhileTheNetworkIsOff()
    {
        var wire = new Tripwire();
        var checker = new UpdateChecker(NetworkGate.CreateHttpClient(NetworkOff, TimeSpan.FromSeconds(5), wire),
            () => UpdateChannel.Stable, "key", new Uri("https://example.com/feed.json"), new Version(1, 0),
            NullLogger<UpdateChecker>.Instance, NetworkOff);

        Assert.False(checker.IsEnabled);
        Assert.Equal(UpdateCheckResult.Disabled, await checker.CheckAsync());
        await Assert.ThrowsAsync<NetworkDisabledException>(() => checker.InstallAsync(
            new UpdateRelease("2.0.0", "stable", "https://example.com/Vox.msi", new string('0', 64), null), _ => Assert.Fail("installer started")));
        Assert.Equal(0, wire.Requests);
    }

    [Fact]
    public async Task TheUpdateChecker_IsStoppedByItsClient_Too()
    {
        // Even given no policy, a client from NetworkGate refuses
        var wire = new Tripwire();
        var checker = new UpdateChecker(NetworkGate.CreateHttpClient(NetworkOff, TimeSpan.FromSeconds(5), wire),
            () => UpdateChannel.Stable, "key", new Uri("https://example.com/feed.json"), new Version(1, 0),
            NullLogger<UpdateChecker>.Instance);

        Assert.Equal(UpdateCheckResult.Failed, await checker.CheckAsync());
        Assert.Equal(0, wire.Requests);
    }

    [Fact]
    public void Brltty_OnAnotherComputer_IsNotContacted_WhileTheNetworkIsOff()
    {
        var logger = new CapturingLogger<BrlttyDisplay>();
        using var display = new BrlttyDisplay("192.0.2.1", connectTimeout: TimeSpan.FromSeconds(30), logger: logger, policy: NetworkOff);

        var started = DateTime.UtcNow;
        Assert.False(display.TryConnect());

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        Assert.Contains("network is turned off", logger.AllText);
    }

    // Types that open connections
    private static readonly Regex NetworkTypes = new(
        @"\b(HttpClient|HttpClientHandler|SocketsHttpHandler|WinHttpHandler|TcpClient|TcpListener|UdpClient|Socket|WebClient|WebRequest|HttpWebRequest|ClientWebSocket|Dns|Ping|SmtpClient|FtpWebRequest)\b");

    private static readonly Regex NewHttpClient = new(@"new\s+(HttpClient|HttpClientHandler|SocketsHttpHandler|WinHttpHandler)\b");

    /// <summary>Each file allowed to use a network type, and why it keeps the guarantee.</summary>
    private static readonly Dictionary<string, string> KnownNetworkUsers = new()
    {
        ["Vox.Core/Enterprise/NetworkGate.cs"] = "the gate itself",
        ["Vox.Core/Updates/UpdateChecker.cs"] = "uses the HttpClient it is given (NetworkGate's) and checks RunPolicy.AllowNetwork",
        ["Vox.Core/Braille/Brltty/BrlttyDisplay.cs"] = "connects only after NetworkGate.MayConnect",
    };

    private static string SourceDirectory =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src"));

    private static IEnumerable<(string File, string Code)> SourceFiles() =>
        Directory.EnumerateFiles(SourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => (Path.GetRelativePath(SourceDirectory, f).Replace(Path.DirectorySeparatorChar, '/'), WithoutComments(File.ReadAllText(f))));

    private static string WithoutComments(string code) =>
        string.Join('\n', code.Split('\n').Select(line =>
        {
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            return comment >= 0 ? line[..comment] : line;
        }));

    [Fact]
    public void EveryNetworkUser_IsKnownAndGated()
    {
        var users = SourceFiles().Where(f => NetworkTypes.IsMatch(f.Code)).Select(f => f.File).Order().ToList();

        Assert.Equal(KnownNetworkUsers.Keys.Order(), users);
    }

    [Fact]
    public void HttpClients_AreMadeOnlyByNetworkGate()
    {
        var makers = SourceFiles().Where(f => NewHttpClient.IsMatch(f.Code)).Select(f => f.File).ToList();

        Assert.Equal(["Vox.Core/Enterprise/NetworkGate.cs"], makers);
        Assert.Contains(SourceFiles(), f => f.File == "Vox.App/ServiceRegistration.cs" && f.Code.Contains("NetworkGate.CreateHttpClient"));
    }

    [Fact]
    public void Sockets_AreOpenedOnlyAfterAsking()
    {
        var brltty = SourceFiles().Single(f => f.File == "Vox.Core/Braille/Brltty/BrlttyDisplay.cs").Code;

        Assert.True(brltty.IndexOf("NetworkGate.MayConnect", StringComparison.Ordinal) < brltty.IndexOf("new TcpClient", StringComparison.Ordinal));
    }
}
