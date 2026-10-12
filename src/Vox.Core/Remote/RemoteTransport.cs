using System.Net;
using System.Net.Sockets;
using System.Text;
using Vox.Core.Enterprise;
using Vox.Core.Lifecycle;

namespace Vox.Core.Remote;

/// <summary>How the two computers reach each other.</summary>
/// <param name="Relay">Through a relay server (both connect out to it) rather than directly.</param>
/// <param name="Host">Direct, controlling side: the controlled computer's address. Relay: the relay server's. Unused when waiting directly.</param>
/// <param name="Port">The port to connect to, or to listen on.</param>
public sealed record RemoteEndpoint(bool Relay, string? Host, int Port)
{
    public const int DefaultPort = 6837;

    /// <summary>"host" or "host:port" (IPv6 in brackets) as typed; null when it isn't an address.</summary>
    public static (string Host, int Port)? ParseAddress(string? text, int defaultPort)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim();
        if (Uri.TryCreate("tcp://" + text, UriKind.Absolute, out var uri) && uri.Host.Length > 0 && uri.AbsolutePath is "/" or "")
            return (uri.Host.Trim('[', ']'), uri.IsDefaultPort || uri.Port <= 0 ? defaultPort : uri.Port);
        return null;
    }
}

/// <summary>
/// Opens the connection a remote session runs over: listening for the controlling computer, or
/// connecting to the controlled one, either directly or through a relay (<see cref="RemoteRelayServer"/>:
/// both sides connect out, which works behind routers and firewalls). Nothing leaves this computer
/// without <see cref="NetworkGate.MayConnect"/>, and nothing listens while the network is off.
/// </summary>
public sealed class RemoteTransport(RunPolicy policy)
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Waits for one controlling computer on <paramref name="port"/> (all addresses) and returns its connection.</summary>
    public async Task<Stream> ListenAsync(int port, CancellationToken cancellationToken)
    {
        if (!policy.AllowNetwork)
            throw new NetworkDisabledException(null);
        var listener = Listener(port);
        listener.Start(1);
        try
        {
            var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            client.NoDelay = true;
            return client.GetStream();
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Connects to <paramref name="host"/>:<paramref name="port"/> (the controlled computer, or a relay).</summary>
    public async Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (!NetworkGate.MayConnect(policy, host))
            throw new NetworkDisabledException(host);
        // IPv6 and IPv4 where the computer has IPv6, else IPv4
        var client = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            return client.GetStream();
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>A listener on every address: IPv6 and IPv4 where the computer has IPv6, else IPv4.</summary>
    public static TcpListener Listener(int port, IPAddress? address = null)
    {
        if (address is not null)
            return new TcpListener(address, port);
        if (!Socket.OSSupportsIPv6)
            return new TcpListener(IPAddress.Any, port);
        var listener = new TcpListener(IPAddress.IPv6Any, port);
        listener.Server.DualMode = true;
        return listener;
    }

    /// <summary>
    /// Connects to a relay and asks it for the other side of the session on <paramref name="code"/>'s
    /// channel; returns once the relay has paired the two (the controlled side waits for as long as it takes).
    /// </summary>
    public async Task<Stream> ConnectThroughRelayAsync(string relay, int port, string code, RemoteRole role, CancellationToken cancellationToken)
    {
        var stream = await ConnectAsync(relay, port, cancellationToken).ConfigureAwait(false);
        try
        {
            var request = RemoteRelayServer.Request(RemoteCode.RelayChannel(code), role);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(request), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // The controlling side expects the other one to be waiting already
            if (role == RemoteRole.Controlling)
                wait.CancelAfter(ConnectTimeout);
            var answer = await RemoteRelayServer.ReadLineAsync(stream, wait.Token).ConfigureAwait(false);
            if (answer != RemoteRelayServer.Paired)
                throw new IOException(answer == RemoteRelayServer.Busy ? "The relay already has that side of this session." : "The relay refused the session.");
            return stream;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw new TimeoutException("Nobody is waiting with that session code on the relay.");
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
