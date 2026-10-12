using System.Net;
using Vox.Core.Lifecycle;

namespace Vox.Core.Enterprise;

/// <summary>
/// The only way Vox reaches the network, so one switch (<see cref="RunPolicy.AllowNetwork"/>: the
/// DisableNetwork policy, or the secure screens) guarantees no connection leaves the computer.
/// HTTP goes through <see cref="CreateHttpClient"/>, whose handler refuses every request while the
/// network is off and follows redirects itself so each hop is checked too; a socket is opened only
/// after <see cref="MayConnect"/> (a connection to this computer itself, such as BRLTTY's, never
/// leaves it and is always allowed); a file is read from another computer (a UNC path) only after
/// <see cref="MayRead"/>.
/// <c>NetworkGateTests</c> checks that no other code opens a connection.
/// </summary>
public static class NetworkGate
{
    /// <summary>An HTTP client that sends nothing while <paramref name="policy"/> doesn't allow the network.</summary>
    /// <param name="inner">The handler that does the sending (tests); null: the standard one.</param>
    public static HttpClient CreateHttpClient(RunPolicy policy, TimeSpan timeout, HttpMessageHandler? inner = null) =>
        new(new PolicyGate(policy, inner ?? new SocketsHttpHandler { AllowAutoRedirect = false }), disposeHandler: true) { Timeout = timeout };

    /// <summary>Redirects followed for one request before it fails.</summary>
    public const int MaxRedirects = 5;

    /// <summary>Whether a connection to <paramref name="host"/> may be opened.</summary>
    public static bool MayConnect(RunPolicy policy, string host) => policy.AllowNetwork || IsLoopback(host);

    /// <summary>
    /// Whether <paramref name="file"/> (a <c>file:</c> address) may be read: one on another computer
    /// (<c>\\server\share</c>) is a network connection like any other.
    /// </summary>
    public static bool MayRead(RunPolicy policy, Uri file) => !file.IsUnc || MayConnect(policy, file.Host);

    /// <summary>"localhost" or a loopback address (127.x.x.x, ::1): this computer itself.</summary>
    public static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

    /// <summary>
    /// Checks every request, and follows a GET's redirects itself (the handler below follows none),
    /// checking each hop: a redirect can't reach a host the policy refuses or drop from HTTPS to
    /// HTTP, and carries none of the first request's headers but Accept. Other methods' redirects
    /// are returned as they are, so a request's body and key never go anywhere but where it was sent.
    /// </summary>
    private sealed class PolicyGate(RunPolicy policy, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Check(request);
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            for (int hop = 0; NextHop(request, response) is { } next; hop++)
            {
                response.Dispose();
                if (hop == MaxRedirects)
                    throw new HttpRequestException($"Too many redirects from {request.RequestUri?.Host}");
                Check(next);
                if (hop > 0)
                    request.Dispose();
                request = next;
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            return response;
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Check(request);
            var response = base.Send(request, cancellationToken);
            for (int hop = 0; NextHop(request, response) is { } next; hop++)
            {
                response.Dispose();
                if (hop == MaxRedirects)
                    throw new HttpRequestException($"Too many redirects from {request.RequestUri?.Host}");
                Check(next);
                if (hop > 0)
                    request.Dispose();
                request = next;
                response = base.Send(request, cancellationToken);
            }
            return response;
        }

        private void Check(HttpRequestMessage request)
        {
            if (!MayConnect(policy, request.RequestUri?.Host ?? string.Empty))
                throw new NetworkDisabledException(request.RequestUri?.Host);
        }

        /// <summary>The request a GET's redirect asks for, or null when it isn't one to follow.</summary>
        private static HttpRequestMessage? NextHop(HttpRequestMessage request, HttpResponseMessage response)
        {
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                    or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                || (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
                || response.Headers.Location is not { } location
                || request.RequestUri is not { } from)
                return null;
            var to = location.IsAbsoluteUri ? location : new Uri(from, location);
            if (to.Scheme != Uri.UriSchemeHttps && to.Scheme != Uri.UriSchemeHttp)
                throw new HttpRequestException($"A redirect from {from.Host} to a {to.Scheme} address was refused");
            if (from.Scheme == Uri.UriSchemeHttps && to.Scheme == Uri.UriSchemeHttp && !IsLoopback(to.Host))
                throw new HttpRequestException($"A redirect from HTTPS to HTTP ({to.Host}) was refused");
            var next = new HttpRequestMessage(request.Method, to) { Version = request.Version, VersionPolicy = request.VersionPolicy };
            foreach (var accept in request.Headers.Accept)
                next.Headers.Accept.Add(accept);
            return next;
        }
    }
}

/// <summary>A request refused because the network is off (<see cref="RunPolicy.AllowNetwork"/>).</summary>
public sealed class NetworkDisabledException(string? host)
    : HttpRequestException($"The network is turned off for Vox; {(string.IsNullOrEmpty(host) ? "nothing" : host)} was not contacted");
