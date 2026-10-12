using System.Net;
using System.Net.Sockets;

namespace Vox.Core.Tests.Remote;

internal static class RemoteTestSupport
{
    /// <summary>Two ends of a TCP connection on this computer.</summary>
    public static async Task<(NetworkStream A, NetworkStream B)> ConnectedPairAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        var accept = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var server = await accept;
        listener.Stop();
        return (client.GetStream(), server.GetStream());
    }

    /// <summary>A port nothing listens on just now.</summary>
    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static async Task WaitUntilAsync(Func<bool> condition, string what, int seconds = 20)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException("Timed out waiting for " + what);
            await Task.Delay(20);
        }
    }
}

/// <summary>A stream that records what is written through it and can change the next write.</summary>
internal sealed class TapStream(Stream inner) : Stream
{
    public List<byte[]> Writes { get; } = [];
    public Func<byte[], byte[]>? ChangeNextWrite { get; set; }
    public Stream Inner => inner;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var bytes = buffer.ToArray();
        if (ChangeNextWrite is { } change)
        {
            ChangeNextWrite = null;
            bytes = change(bytes);
        }
        Writes.Add(bytes);
        return inner.WriteAsync(bytes, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }
}

internal static class RemoteTestSupportWait
{
    public static Task UntilAsync(Func<bool> condition) => RemoteTestSupport.WaitUntilAsync(condition, "the condition", 10);
}
