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

    /// <summary>Answers each address from <see cref="Redirects"/> with a redirect, anything else with 200.</summary>
    private sealed class Redirector : HttpMessageHandler
    {
        public Dictionary<string, string> Redirects { get; } = new();
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (!Redirects.TryGetValue(request.RequestUri!.AbsoluteUri, out var to))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("done") });
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri(to, UriKind.RelativeOrAbsolute);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task Redirects_AreFollowed_ByTheGate()
    {
        var wire = new Redirector();
        wire.Redirects["https://example.com/feed.json"] = "https://cdn.example.com/feed.json";
        wire.Redirects["https://cdn.example.com/feed.json"] = "/v2/feed.json";
        using var http = NetworkGate.CreateHttpClient(RunPolicy.Normal, TimeSpan.FromSeconds(5), wire);

        Assert.Equal("done", await http.GetStringAsync("https://example.com/feed.json"));
        Assert.Equal(["https://example.com/feed.json", "https://cdn.example.com/feed.json", "https://cdn.example.com/v2/feed.json"],
            wire.Requests.Select(r => r.RequestUri!.AbsoluteUri));
    }

    [Fact]
    public async Task ARedirect_OffThisComputer_IsRefused_WhileTheNetworkIsOff()
    {
        var wire = new Redirector();
        wire.Redirects["http://localhost:8080/catalogue.json"] = "https://example.com/catalogue.json";
        using var http = NetworkGate.CreateHttpClient(NetworkOff, TimeSpan.FromSeconds(5), wire);

        await Assert.ThrowsAsync<NetworkDisabledException>(() => http.GetStringAsync("http://localhost:8080/catalogue.json"));
        Assert.Single(wire.Requests);
    }

    [Fact]
    public async Task ARedirect_FromHttpsToHttp_IsRefused()
    {
        var wire = new Redirector();
        wire.Redirects["https://example.com/Vox.msi"] = "http://example.com/Vox.msi";
        using var http = NetworkGate.CreateHttpClient(RunPolicy.Normal, TimeSpan.FromSeconds(5), wire);

        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync("https://example.com/Vox.msi"));
        Assert.Single(wire.Requests);
    }

    [Fact]
    public async Task EndlessRedirects_Fail()
    {
        var wire = new Redirector();
        wire.Redirects["https://example.com/a"] = "https://example.com/b";
        wire.Redirects["https://example.com/b"] = "https://example.com/a";
        using var http = NetworkGate.CreateHttpClient(RunPolicy.Normal, TimeSpan.FromSeconds(5), wire);

        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync("https://example.com/a"));
        Assert.Equal(NetworkGate.MaxRedirects + 1, wire.Requests.Count);
    }

    [Fact]
    public async Task APostsRedirect_IsNotFollowed_SoItsBodyAndKeyGoNowhereElse()
    {
        var wire = new Redirector();
        wire.Redirects["https://api.example.com/v1/messages"] = "https://elsewhere.example.com/collect";
        using var http = NetworkGate.CreateHttpClient(RunPolicy.Normal, TimeSpan.FromSeconds(5), wire);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/v1/messages") { Content = new StringContent("{}") };
        request.Headers.Add("x-api-key", "secret");

        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Single(wire.Requests);
    }

    [Fact]
    public async Task ARedirect_CarriesNoHeaders_ButAccept()
    {
        var wire = new Redirector();
        wire.Redirects["https://example.com/feed.json"] = "https://other.example.com/feed.json";
        using var http = NetworkGate.CreateHttpClient(RunPolicy.Normal, TimeSpan.FromSeconds(5), wire);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/feed.json");
        request.Headers.Add("x-api-key", "secret");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request);

        var hop = wire.Requests[1];
        Assert.False(hop.Headers.Contains("x-api-key"));
        Assert.Equal("application/json", Assert.Single(hop.Headers.Accept).MediaType);
    }

    [Theory]
    [InlineData(@"\\files.example.com\share\catalogue.json", false)]
    [InlineData("file://files.example.com/share/catalogue.json", false)]
    [InlineData(@"C:\Vox\catalogue.json", true)]
    [InlineData("file:///C:/Vox/catalogue.json", true)]
    public void AFileOnAnotherComputer_IsTheNetwork(string address, bool readable)
    {
        var file = new Uri(address);

        Assert.Equal(readable, NetworkGate.MayRead(NetworkOff, file));
        Assert.True(NetworkGate.MayRead(RunPolicy.Normal, file));
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

    [Fact]
    public async Task TheCloudDescriber_IsStoppedByItsClient_Too()
    {
        var wire = new Tripwire();
        var settings = new Moq.Mock<Microsoft.Extensions.Options.IOptionsMonitor<Vox.Core.Configuration.VoxSettings>>();
        settings.Setup(m => m.CurrentValue).Returns(new Vox.Core.Configuration.VoxSettings { AiCloudProvider = Vox.Core.Ai.AiCloudProvider.Claude });
        var describer = new Vox.Core.Ai.CloudImageDescriber(NetworkGate.CreateHttpClient(NetworkOff, TimeSpan.FromSeconds(5), wire),
            settings.Object, new Ai.FakeKeyStore("key"), NullLogger<Vox.Core.Ai.CloudImageDescriber>.Instance);

        var failed = await Assert.ThrowsAsync<Vox.Core.Ai.AiException>(() =>
            describer.DescribeAsync(new Vox.Core.Ai.AiRequest(Vox.Core.Ai.AiTask.SummarizePage, Context: "text")));

        Assert.Equal(Vox.Core.Ai.AiFailure.Failed, failed.Failure);
        Assert.Equal(0, wire.Requests);
    }

    [Theory]
    [InlineData("http://192.0.2.1:11434")]
    [InlineData("https://models.example.com")]
    public void TheLocalDescriber_NeverUsesAnotherComputer(string endpoint)
    {
        var settings = new Moq.Mock<Microsoft.Extensions.Options.IOptionsMonitor<Vox.Core.Configuration.VoxSettings>>();
        settings.Setup(m => m.CurrentValue).Returns(new Vox.Core.Configuration.VoxSettings { AiLocalModel = "llava", AiLocalEndpoint = endpoint });
        var describer = new Vox.Core.Ai.LocalImageDescriber(NetworkGate.CreateHttpClient(RunPolicy.Normal, TimeSpan.FromSeconds(5), new Tripwire()),
            settings.Object, NullLogger<Vox.Core.Ai.LocalImageDescriber>.Instance);

        Assert.False(describer.IsAvailable);
        Assert.Null(describer.Endpoint);
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
        ["Vox.Core/Ai/CloudImageDescriber.cs"] = "uses the HttpClient it is given (NetworkGate's); AiConsent refuses while the network is off",
        ["Vox.Core/Ai/LocalImageDescriber.cs"] = "uses the HttpClient it is given (NetworkGate's) and only ever a loopback address",
        ["Vox.Core/AddOns/AddOnStore.cs"] = "uses the HttpClient it is given (NetworkGate's); a catalogue on this computer is read from disk",
        ["Vox.Core/Remote/RemoteTransport.cs"] = "connects only after NetworkGate.MayConnect and listens only while RunPolicy.AllowNetwork",
        ["Vox.Core/Remote/RemoteRelayServer.cs"] = "the relay server, run on its own by Vox.App --remote-relay only while RunPolicy.AllowNetwork",
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
        var remote = SourceFiles().Single(f => f.File == "Vox.Core/Remote/RemoteTransport.cs").Code;
        Assert.True(remote.IndexOf("NetworkGate.MayConnect", StringComparison.Ordinal) < remote.IndexOf("new TcpClient", StringComparison.Ordinal));
        Assert.True(remote.IndexOf("policy.AllowNetwork", StringComparison.Ordinal) < remote.IndexOf("new TcpListener", StringComparison.Ordinal));
        var relay = SourceFiles().Single(f => f.File == "Vox.App/Program.cs").Code;
        Assert.True(relay.IndexOf("policy.AllowNetwork", StringComparison.Ordinal) < relay.IndexOf("RemoteRelayServer(", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemoteAccess_ConnectsNowhere_WhileTheNetworkIsOff()
    {
        var transport = new Vox.Core.Remote.RemoteTransport(NetworkOff);

        await Assert.ThrowsAsync<NetworkDisabledException>(() => transport.ConnectAsync("192.0.2.1", 6837, CancellationToken.None));
        await Assert.ThrowsAsync<NetworkDisabledException>(() => transport.ListenAsync(0, CancellationToken.None));
        await Assert.ThrowsAsync<NetworkDisabledException>(() => new Vox.Core.Remote.RemoteTransport(RunPolicy.Secure).ConnectAsync("192.0.2.1", 6837, CancellationToken.None));
    }
}
