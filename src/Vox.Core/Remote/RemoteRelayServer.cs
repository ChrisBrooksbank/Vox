using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Vox.Core.Remote;

/// <summary>
/// A relay for remote sessions between computers that can't reach each other directly: both
/// connect out to it, each sends "VOXRELAY 1 &lt;channel&gt; &lt;controlled|controlling&gt;", and once
/// both sides of a channel are there it answers "OK" to each and copies bytes between them. It only
/// ever sees the channel (a slow hash of the session code) and encrypted frames: it can't read or
/// change the session. Run with <c>Vox.App --remote-relay [port]</c>.
/// </summary>
public sealed class RemoteRelayServer(ILogger logger, TimeSpan? waitLimit = null) : IDisposable
{
    public const string Paired = "OK";
    public const string Busy = "BUSY";
    public const string Refused = "NO";
    private const int MaxLine = 128;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private const int MaxWaiting = 1000;

    private readonly Dictionary<string, Waiting> _waiting = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly TimeSpan _waitLimit = waitLimit ?? TimeSpan.FromMinutes(30);
    private TcpListener? _listener;

    private sealed record Waiting(RemoteRole Role, TcpClient Client, TaskCompletionSource<TcpClient> Partner);

    /// <summary>The request line a client sends.</summary>
    public static string Request(string channel, RemoteRole role) =>
        $"VOXRELAY 1 {channel} {(role == RemoteRole.Controlled ? "controlled" : "controlling")}\n";

    /// <summary>The port it listens on once started (the one given, or the one chosen for port 0).</summary>
    public int Port => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;

    /// <summary>Starts listening; <see cref="RunAsync"/> serves.</summary>
    public void Start(int port, IPAddress? address = null)
    {
        _listener = RemoteTransport.Listener(port, address);
        _listener.Start();
        logger.LogInformation("Remote relay listening on port {Port}", Port);
    }

    /// <summary>Accepts connections until stopped.</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var listener = _listener ?? throw new InvalidOperationException("Start the relay first");
        while (!linked.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(linked.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            client.NoDelay = true;
            _ = ServeAsync(client, linked.Token);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        string? channel = null;
        Waiting? mine = null;
        try
        {
            var stream = client.GetStream();
            string? line;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(RequestTimeout);
                line = await ReadLineAsync(stream, timeout.Token).ConfigureAwait(false);
            }
            var parts = line?.Split(' ');
            if (parts is not ["VOXRELAY", "1", var requested, var roleName] || requested.Length != 32 || !requested.All(Uri.IsHexDigit)
                || roleName is not ("controlled" or "controlling"))
            {
                await AnswerAsync(stream, Refused, cancellationToken).ConfigureAwait(false);
                client.Dispose();
                return;
            }
            channel = requested;
            var role = roleName == "controlled" ? RemoteRole.Controlled : RemoteRole.Controlling;

            TcpClient? partner = null;
            lock (_lock)
            {
                if (_waiting.TryGetValue(channel, out var other))
                {
                    if (other.Role == role)
                    {
                        partner = null;
                        mine = null;
                        channel = null;
                    }
                    else
                    {
                        _waiting.Remove(channel);
                        other.Partner.TrySetResult(client);
                        partner = other.Client;
                    }
                }
                else if (_waiting.Count < MaxWaiting)
                {
                    mine = new Waiting(role, client, new TaskCompletionSource<TcpClient>(TaskCreationOptions.RunContinuationsAsynchronously));
                    _waiting[channel] = mine;
                }
            }
            if (channel is null || (partner is null && mine is null))
            {
                await AnswerAsync(stream, channel is null ? Busy : Refused, cancellationToken).ConfigureAwait(false);
                client.Dispose();
                return;
            }
            if (partner is not null)
                return; // The waiting side's task pipes both ways

            TcpClient other2;
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                wait.CancelAfter(_waitLimit);
                other2 = await mine!.Partner.Task.WaitAsync(wait.Token).ConfigureAwait(false);
            }
            var otherStream = other2.GetStream();
            await AnswerAsync(stream, Paired, cancellationToken).ConfigureAwait(false);
            await AnswerAsync(otherStream, Paired, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Remote relay paired a session");
            using (client)
            using (other2)
            {
                var a = stream.CopyToAsync(otherStream, cancellationToken);
                var b = otherStream.CopyToAsync(stream, cancellationToken);
                await Task.WhenAny(a, b).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            client.Dispose();
        }
        finally
        {
            if (mine is not null && channel is not null)
            {
                lock (_lock)
                {
                    if (_waiting.TryGetValue(channel, out var waiting) && waiting == mine)
                        _waiting.Remove(channel);
                }
            }
        }
    }

    private static async Task AnswerAsync(Stream stream, string answer, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(answer + "\n"), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one short ASCII line (without its newline) a byte at a time, so nothing after it is consumed.</summary>
    public static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (bytes.Count < MaxLine)
        {
            if (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0)
                return null;
            if (one[0] == (byte)'\n')
                return Encoding.ASCII.GetString(bytes.ToArray());
            bytes.Add(one[0]);
        }
        return null;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener?.Stop();
        lock (_lock)
        {
            foreach (var waiting in _waiting.Values)
                waiting.Client.Dispose();
            _waiting.Clear();
        }
        _stop.Dispose();
    }
}
