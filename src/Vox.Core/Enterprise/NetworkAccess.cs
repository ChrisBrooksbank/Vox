using System.Net;
using Vox.Core.Lifecycle;

namespace Vox.Core.Enterprise;

/// <summary>
/// The only way Vox reaches the network, so one switch (<see cref="RunPolicy.AllowNetwork"/>: the
/// DisableNetwork policy, or the secure screens) guarantees no connection leaves the computer.
/// HTTP goes through <see cref="CreateHttpClient"/>, whose handler refuses every request while the
/// network is off; a socket is opened only after <see cref="MayConnect"/> (a connection to this
/// computer itself, such as BRLTTY's, never leaves it and is always allowed).
/// <c>NetworkAccessTests</c> checks that no other code opens a connection.
/// </summary>
public static class NetworkAccess
{
    /// <summary>An HTTP client that sends nothing while <paramref name="policy"/> doesn't allow the network.</summary>
    /// <param name="inner">The handler that does the sending (tests); null: the standard one.</param>
    public static HttpClient CreateHttpClient(RunPolicy policy, TimeSpan timeout, HttpMessageHandler? inner = null) =>
        new(new PolicyGate(policy, inner ?? new SocketsHttpHandler()), disposeHandler: true) { Timeout = timeout };

    /// <summary>Whether a connection to <paramref name="host"/> may be opened.</summary>
    public static bool MayConnect(RunPolicy policy, string host) => policy.AllowNetwork || IsLoopback(host);

    /// <summary>"localhost" or a loopback address (127.x.x.x, ::1): this computer itself.</summary>
    public static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

    private sealed class PolicyGate(RunPolicy policy, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!MayConnect(policy, request.RequestUri?.Host ?? string.Empty))
                throw new NetworkDisabledException(request.RequestUri?.Host);
            return base.SendAsync(request, cancellationToken);
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!MayConnect(policy, request.RequestUri?.Host ?? string.Empty))
                throw new NetworkDisabledException(request.RequestUri?.Host);
            return base.Send(request, cancellationToken);
        }
    }
}

/// <summary>A request refused because the network is off (<see cref="RunPolicy.AllowNetwork"/>).</summary>
public sealed class NetworkDisabledException(string? host)
    : HttpRequestException($"The network is turned off for Vox; {(string.IsNullOrEmpty(host) ? "nothing" : host)} was not contacted");
