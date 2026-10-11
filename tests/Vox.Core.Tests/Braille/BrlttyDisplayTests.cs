using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Vox.Core.Braille;
using Vox.Core.Braille.Brltty;
using Xunit;

namespace Vox.Core.Tests.Braille;

public sealed class BrlttyDisplayTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private readonly string _keyFile = Path.Combine(Path.GetTempPath(), $"vox-brlapi-{Guid.NewGuid():N}.key");

    public BrlttyDisplayTests()
    {
        File.WriteAllText(_keyFile, "secret-key");
    }

    public void Dispose()
    {
        File.Delete(_keyFile);
    }

    private BrlttyDisplay CreateDisplay(FakeBrlApiServer server, params string[] keyFiles) =>
        new("127.0.0.1", server.Port, keyFiles.Length == 0 ? [_keyFile] : keyFiles, TimeSpan.FromSeconds(5));

    [Fact]
    public void TryConnect_HandshakesWithKeyAuthAndReadsTheDisplaySize()
    {
        using var server = new FakeBrlApiServer(columns: 40, expectedKey: "secret-key");
        using var display = CreateDisplay(server);

        Assert.True(display.TryConnect());

        Assert.True(display.IsConnected);
        Assert.Equal(40, display.CellCount);
        Assert.Equal("bm", display.DriverName);
        Assert.Equal("BRLTTY", display.Id);
        Assert.Equal("BRLTTY", display.Name);
        var auth = server.Received(BrlApiPacketType.Auth);
        Assert.Equal(BrlApiProtocol.AuthKey, BinaryPrimitives.ReadUInt32BigEndian(auth.Payload));
        Assert.Equal("secret-key"u8.ToArray(), auth.Payload[4..]);
        var tty = server.Received(BrlApiPacketType.EnterTtyMode);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0 }, tty.Payload);
        Assert.True(display.TryConnect()); // already connected
    }

    [Fact]
    public void TryConnect_FailsWithoutThrowingWhenTheKeyIsWrong()
    {
        var wrongKey = Path.Combine(Path.GetTempPath(), $"vox-brlapi-{Guid.NewGuid():N}.key");
        File.WriteAllText(wrongKey, "wrong");
        try
        {
            using var server = new FakeBrlApiServer(columns: 40, expectedKey: "secret-key");
            using var display = CreateDisplay(server, wrongKey);

            Assert.False(display.TryConnect());
            Assert.False(display.IsConnected);
            Assert.Equal(0, display.CellCount);
        }
        finally
        {
            File.Delete(wrongKey);
        }
    }

    [Fact]
    public void TryConnect_WorksWithoutAuthentication()
    {
        using var server = new FakeBrlApiServer(columns: 20, expectedKey: null);
        using var display = CreateDisplay(server, Path.Combine(Path.GetTempPath(), "no-such-brlapi.key"));

        Assert.True(display.TryConnect());
        Assert.Equal(20, display.CellCount);
    }

    [Fact]
    public void TryConnect_IsFalseWhenNoServerListens()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var display = new BrlttyDisplay("127.0.0.1", port, [_keyFile]);

        Assert.False(display.TryConnect());
        Assert.False(display.IsConnected);
    }

    [Fact]
    public void Write_SendsDotsAndThenOnlyTheChangedCells()
    {
        using var server = new FakeBrlApiServer(columns: 4, expectedKey: "secret-key");
        using var display = CreateDisplay(server);
        Assert.True(display.TryConnect());

        display.Write(new byte[] { 0x01, 0x03 }, cursorPosition: 1);
        var first = server.Received(BrlApiPacketType.Write);
        Assert.Equal(BrlApiProtocol.EncodeWriteDots(1, new byte[] { 0x01, 0x03, 0, 0 })[8..], first.Payload);

        display.Write(new byte[] { 0x01, 0x03 }, cursorPosition: 1); // unchanged: nothing sent
        display.Write(new byte[] { 0x01, 0x03, 0x09 }, cursorPosition: null);
        var second = server.Received(BrlApiPacketType.Write);
        Assert.Equal(BrlApiProtocol.EncodeWriteDots(3, new byte[] { 0x09 })[8..], second.Payload);
    }

    [Fact]
    public void KeyPacket_RaisesKeyPressedWithTheRoutingCell()
    {
        using var server = new FakeBrlApiServer(columns: 40, expectedKey: "secret-key");
        using var display = CreateDisplay(server);
        var keys = new BlockingCollection<BrailleKeyEventArgs>();
        display.KeyPressed += (_, e) => keys.Add(e);
        Assert.True(display.TryConnect());

        server.SendKey(BrlApiKeyCodes.TypeCommand | (1UL << 16) | 7);
        server.SendKey(BrlApiKeyCodes.TypeCommand | 23);

        Assert.True(keys.TryTake(out var routing, Wait));
        Assert.Equal(new[] { "routing" }, routing.Keys);
        Assert.Equal(7, routing.RoutingCell);
        Assert.True(keys.TryTake(out var pan, Wait));
        Assert.Equal(new[] { "panLeft" }, pan.Keys);
    }

    [Fact]
    public void ServerClosing_RaisesDisconnectedOnce()
    {
        using var server = new FakeBrlApiServer(columns: 40, expectedKey: "secret-key");
        using var display = CreateDisplay(server);
        var disconnects = 0;
        using var disconnected = new ManualResetEventSlim();
        display.Disconnected += (_, _) =>
        {
            Interlocked.Increment(ref disconnects);
            disconnected.Set();
        };
        Assert.True(display.TryConnect());

        server.CloseClient();

        Assert.True(disconnected.Wait(Wait));
        Assert.False(display.IsConnected);
        Assert.Equal(0, display.CellCount);
        display.Write(new byte[] { 0x01 }, null); // ignored, no throw
        Thread.Sleep(50);
        Assert.Equal(1, disconnects);
    }

    [Fact]
    public void Dispose_LeavesTtyModeWithoutRaisingDisconnected()
    {
        using var server = new FakeBrlApiServer(columns: 40, expectedKey: "secret-key");
        var display = CreateDisplay(server);
        var disconnected = false;
        display.Disconnected += (_, _) => disconnected = true;
        Assert.True(display.TryConnect());

        display.Dispose();

        server.Received(BrlApiPacketType.LeaveTtyMode);
        Assert.False(display.IsConnected);
        Assert.False(display.TryConnect());
        Assert.False(disconnected);
    }

    /// <summary>A BrlAPI server on a loopback port: one client, key or no authentication, records what it receives.</summary>
    private sealed class FakeBrlApiServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly int _columns;
        private readonly string? _expectedKey;
        private readonly BlockingCollection<BrlApiPacket> _received = new();
        private readonly Thread _thread;
        private readonly object _sendLock = new();
        private TcpClient? _client;
        private NetworkStream? _stream;

        public FakeBrlApiServer(int columns, string? expectedKey)
        {
            _columns = columns;
            _expectedKey = expectedKey;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _thread = new Thread(Serve) { IsBackground = true };
            _thread.Start();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        /// <summary>The next packet of <paramref name="type"/> the client sent (others are skipped).</summary>
        public BrlApiPacket Received(BrlApiPacketType type)
        {
            while (_received.TryTake(out var packet, Wait))
            {
                if (packet.Type == type)
                    return packet;
            }
            throw new TimeoutException($"No {type} packet arrived.");
        }

        public void SendKey(ulong code)
        {
            var payload = new byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(payload, code);
            Send(BrlApiPacketType.Key, payload);
        }

        public void CloseClient() => _client?.Close();

        private void Send(BrlApiPacketType type, byte[] payload)
        {
            lock (_sendLock)
                _stream!.Write(BrlApiProtocol.Encode(type, payload));
        }

        private static byte[] U32s(params uint[] values)
        {
            var bytes = new byte[4 * values.Length];
            for (var i = 0; i < values.Length; i++)
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4 * i), values[i]);
            return bytes;
        }

        private void Serve()
        {
            try
            {
                _client = _listener.AcceptTcpClient();
                _stream = _client.GetStream();
                Send(BrlApiPacketType.Version, U32s(BrlApiProtocol.ProtocolVersion));
                var version = Next();
                if (version?.Type != BrlApiPacketType.Version)
                    return;

                if (_expectedKey is null)
                {
                    Send(BrlApiPacketType.Auth, U32s(BrlApiProtocol.AuthNone));
                }
                else
                {
                    Send(BrlApiPacketType.Auth, U32s(BrlApiProtocol.AuthKey));
                    var auth = Next();
                    if (auth is null || System.Text.Encoding.ASCII.GetString(auth.Payload, 4, auth.Payload.Length - 4) != _expectedKey)
                    {
                        Send(BrlApiPacketType.Error, U32s(17));
                        _client.Close();
                        return;
                    }
                    Send(BrlApiPacketType.Ack, []);
                }

                while (Next() is { } packet)
                {
                    switch (packet.Type)
                    {
                        case BrlApiPacketType.GetDriverName:
                            Send(BrlApiPacketType.GetDriverName, "bm\0"u8.ToArray());
                            break;
                        case BrlApiPacketType.GetDisplaySize:
                            Send(BrlApiPacketType.GetDisplaySize, U32s((uint)_columns, 1));
                            break;
                        case BrlApiPacketType.EnterTtyMode:
                        case BrlApiPacketType.LeaveTtyMode:
                            Send(BrlApiPacketType.Ack, []);
                            break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                // The client went away or the test ended.
            }
        }

        private BrlApiPacket? Next()
        {
            var packet = BrlApiProtocol.ReadPacket(_stream!);
            if (packet is not null)
                _received.Add(packet);
            return packet;
        }

        public void Dispose()
        {
            _client?.Close();
            _listener.Stop();
            _thread.Join(Wait);
            _received.Dispose();
        }
    }
}
