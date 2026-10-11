using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Vox.Core.Braille.Brltty;

/// <summary>
/// A braille display driven by BRLTTY, through its BrlAPI server (an optional component the user
/// installs; <c>docs/brltty.md</c>). Speaks the BrlAPI protocol over TCP itself
/// (<see cref="BrlApiProtocol"/>): connects, authenticates with the server's key file, takes over
/// the display in tty mode with keys as BrlAPI commands, writes cells as raw dots and reports keys
/// (<see cref="BrlApiKeyCodes"/>) from its own reader thread.
/// </summary>
public sealed class BrlttyDisplay : IBrailleDisplayDriver
{
    /// <summary>How long connecting and the handshake may take altogether.</summary>
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Where BRLTTY for Windows usually keeps the BrlAPI key file: the <c>BRLAPI_AUTH</c> variable
    /// when it names a file, else <c>etc\brlapi.key</c> under the usual install folders.
    /// </summary>
    public static IReadOnlyList<string> DefaultKeyFilePaths()
    {
        var paths = new List<string>();
        var auth = Environment.GetEnvironmentVariable("BRLAPI_AUTH");
        if (!string.IsNullOrWhiteSpace(auth))
            paths.Add(auth.StartsWith("keyfile:", StringComparison.OrdinalIgnoreCase) ? auth["keyfile:".Length..] : auth);
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                 })
        {
            if (!string.IsNullOrEmpty(root))
                paths.Add(Path.Combine(root, "BRLTTY", "etc", "brlapi.key"));
        }
        paths.Add(@"C:\brltty\etc\brlapi.key");
        paths.Add("/etc/brlapi.key");
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private readonly string _host;
    private readonly int _port;
    private readonly IReadOnlyList<string> _keyFilePaths;
    private readonly TimeSpan _connectTimeout;
    private readonly ILogger<BrlttyDisplay>? _logger;
    private readonly Lifecycle.RunPolicy _policy;

    private readonly object _connectLock = new();
    private readonly object _writeLock = new();
    private Connection? _connection;
    private volatile bool _disposed;

    /// <summary>Creates the driver; nothing is opened until <see cref="TryConnect"/>.</summary>
    /// <param name="host">The BrlAPI server's host (default 127.0.0.1; <see cref="BrlApiProtocol.ParseHost"/> reads brlapi host strings).</param>
    /// <param name="port">Its TCP port (4101 for display <c>:0</c>).</param>
    /// <param name="keyFilePaths">Key files to try, first existing one wins; null for <see cref="DefaultKeyFilePaths"/>.</param>
    /// <param name="connectTimeout">The time connecting and the handshake may take; null for about a second.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="policy">
    /// What this Vox may do: a server on another computer is connected to only while it allows the
    /// network (<see cref="Enterprise.NetworkAccess.MayConnect"/>); null: <see cref="Lifecycle.RunPolicy.Normal"/>.
    /// </param>
    public BrlttyDisplay(
        string host = "127.0.0.1",
        int port = BrlApiProtocol.DefaultPort,
        IReadOnlyList<string>? keyFilePaths = null,
        TimeSpan? connectTimeout = null,
        ILogger<BrlttyDisplay>? logger = null,
        Lifecycle.RunPolicy? policy = null)
    {
        _policy = policy ?? Lifecycle.RunPolicy.Normal;
        _host = host;
        _port = port;
        _keyFilePaths = keyFilePaths ?? DefaultKeyFilePaths();
        _connectTimeout = connectTimeout ?? DefaultConnectTimeout;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Id => "BRLTTY";

    /// <inheritdoc />
    public string Name => "BRLTTY";

    /// <inheritdoc />
    public bool IsConnected => _connection is { IsLost: false };

    /// <inheritdoc />
    public int CellCount => _connection is { IsLost: false } c ? c.CellCount : 0;

    /// <summary>The BRLTTY braille driver's name ("bm", "eu", ...) of the connected display, if known.</summary>
    public string? DriverName => _connection?.DriverName;

    /// <inheritdoc />
    public event EventHandler<BrailleKeyEventArgs>? KeyPressed;

    /// <inheritdoc />
    public event EventHandler? Disconnected;

    /// <inheritdoc />
    public bool TryConnect()
    {
        lock (_connectLock)
        {
            if (_disposed)
                return false;
            if (IsConnected)
                return true;
            _connection?.Close();
            _connection = null;

            if (!Enterprise.NetworkAccess.MayConnect(_policy, _host))
            {
                _logger?.LogDebug("Not connecting to BRLTTY at {Host}: the network is turned off", _host);
                return false;
            }

            TcpClient? client = null;
            try
            {
                var deadline = Stopwatch.StartNew();
                client = new TcpClient { NoDelay = true };
                using (var cts = new CancellationTokenSource(_connectTimeout))
                {
                    client.ConnectAsync(_host, _port, cts.Token).AsTask().GetAwaiter().GetResult();
                }
                var stream = client.GetStream();
                var connection = new Connection(this, client, stream);
                connection.Handshake(deadline, _connectTimeout);
                client.ReceiveTimeout = 0;
                _connection = connection;
                client = null;
                connection.Start();
                _logger?.LogInformation(
                    "Connected to BRLTTY at {Host}:{Port}: {Cells} cells (driver {Driver})",
                    _host, _port, connection.CellCount, connection.DriverName ?? "unknown");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("No BRLTTY BrlAPI server at {Host}:{Port}: {Message}", _host, _port, ex.Message);
                client?.Dispose();
                return false;
            }
        }
    }

    /// <inheritdoc />
    public void Write(ReadOnlyMemory<byte> cells, int? cursorPosition)
    {
        var connection = _connection;
        if (connection is null || connection.IsLost)
            return;
        connection.Write(cells.Span);
    }

    /// <summary>Leaves tty mode and closes the connection; <see cref="TryConnect"/> may open it again.</summary>
    public void Disconnect()
    {
        lock (_connectLock)
        {
            var connection = _connection;
            _connection = null;
            if (connection is null)
                return;
            connection.Send(BrlApiProtocol.EncodeEmpty(BrlApiPacketType.LeaveTtyMode));
            connection.Close();
        }
    }

    /// <summary>Leaves tty mode, so BRLTTY shows its own screen again, and closes the connection.</summary>
    public void Dispose()
    {
        lock (_connectLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            var connection = _connection;
            _connection = null;
            if (connection is null)
                return;
            connection.Send(BrlApiProtocol.EncodeEmpty(BrlApiPacketType.LeaveTtyMode));
            connection.Close();
        }
    }

    private void RaiseKey(BrailleKeyEventArgs args)
    {
        try
        {
            KeyPressed?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "A braille key handler failed");
        }
    }

    private void RaiseDisconnected()
    {
        if (_disposed)
            return;
        _logger?.LogInformation("Lost the connection to BRLTTY");
        try
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "A braille disconnect handler failed");
        }
    }

    /// <summary>One BrlAPI session: the socket, the display's size, the reader thread and the last cells written.</summary>
    private sealed class Connection(BrlttyDisplay owner, TcpClient client, NetworkStream stream)
    {
        private byte[]? _shown;
        private int _lost;
        private Thread? _reader;

        public int CellCount { get; private set; }
        public string? DriverName { get; private set; }
        public bool IsLost => Volatile.Read(ref _lost) != 0;

        public void Handshake(Stopwatch elapsed, TimeSpan timeout)
        {
            BrlApiPacket Expect(BrlApiPacketType type)
            {
                var remaining = timeout - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    throw new TimeoutException("The BrlAPI handshake took too long.");
                client.ReceiveTimeout = Math.Max(1, (int)remaining.TotalMilliseconds);
                var packet = BrlApiProtocol.ReadPacket(stream)
                             ?? throw new EndOfStreamException("BRLTTY closed the connection.");
                if (packet.Type == BrlApiPacketType.Error)
                    throw new BrlApiProtocolException(
                        $"BRLTTY refused the request: {BrlApiProtocol.ErrorName(BrlApiProtocol.DecodeError(packet.Payload))}.");
                if (packet.Type == BrlApiPacketType.Exception)
                    throw new BrlApiProtocolException(
                        $"BRLTTY reported {BrlApiProtocol.ErrorName(BrlApiProtocol.DecodeException(packet.Payload).Code)}.");
                if (packet.Type != type)
                    throw new BrlApiProtocolException(
                        $"Expected a BrlAPI {BrlApiProtocol.Describe(type)} packet, got {BrlApiProtocol.Describe(packet.Type)}.");
                return packet;
            }

            var version = BrlApiProtocol.DecodeVersion(Expect(BrlApiPacketType.Version).Payload);
            if (version != BrlApiProtocol.ProtocolVersion)
                throw new BrlApiProtocolException($"BRLTTY speaks BrlAPI protocol {version}, Vox speaks {BrlApiProtocol.ProtocolVersion}.");
            Send(BrlApiProtocol.EncodeVersion());

            var methods = BrlApiProtocol.DecodeAuthMethods(Expect(BrlApiPacketType.Auth).Payload);
            if (!Authenticate(methods, Expect))
                throw new BrlApiProtocolException("BRLTTY did not accept any authentication method Vox offers.");

            Send(BrlApiProtocol.EncodeEmpty(BrlApiPacketType.GetDriverName));
            try
            {
                DriverName = BrlApiProtocol.DecodeString(Expect(BrlApiPacketType.GetDriverName).Payload);
            }
            catch (BrlApiProtocolException)
            {
                DriverName = null; // only for logs; a server that won't say still works
            }

            Send(BrlApiProtocol.EncodeEmpty(BrlApiPacketType.GetDisplaySize));
            var (columns, rows) = BrlApiProtocol.DecodeDisplaySize(Expect(BrlApiPacketType.GetDisplaySize).Payload);
            if (columns <= 0)
                throw new BrlApiProtocolException("BRLTTY has no braille display.");
            CellCount = columns * Math.Max(1, rows);

            Send(BrlApiProtocol.EncodeEnterTtyMode([]));
            Expect(BrlApiPacketType.Ack);
        }

        private bool Authenticate(IReadOnlyList<uint> methods, Func<BrlApiPacketType, BrlApiPacket> expect)
        {
            foreach (var method in methods)
            {
                switch (method)
                {
                    case BrlApiProtocol.AuthNone:
                        return true;
                    case BrlApiProtocol.AuthKey:
                        var key = ReadKey();
                        if (key is null)
                            continue;
                        Send(BrlApiProtocol.EncodeAuthKey(key));
                        if (TryAck(expect))
                            return true;
                        break;
                    case BrlApiProtocol.AuthCred:
                        Send(BrlApiProtocol.EncodeAuth(BrlApiProtocol.AuthCred));
                        if (TryAck(expect))
                            return true;
                        break;
                }
            }
            return false;
        }

        private static bool TryAck(Func<BrlApiPacketType, BrlApiPacket> expect)
        {
            try
            {
                expect(BrlApiPacketType.Ack);
                return true;
            }
            catch (BrlApiProtocolException)
            {
                return false;
            }
        }

        private byte[]? ReadKey()
        {
            foreach (var path in owner._keyFilePaths)
            {
                try
                {
                    if (!File.Exists(path))
                        continue;
                    var key = File.ReadAllBytes(path);
                    if (key.Length > BrlApiProtocol.MaxPacketSize - 4)
                        key = key[..(BrlApiProtocol.MaxPacketSize - 4)];
                    return key;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    owner._logger?.LogDebug("Can't read the BrlAPI key file {Path}: {Message}", path, ex.Message);
                }
            }
            owner._logger?.LogDebug("BRLTTY asks for a key file and none was found");
            return null;
        }

        public void Start()
        {
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "BRLTTY reader" };
            _reader.Start();
        }

        private void ReadLoop()
        {
            try
            {
                while (!IsLost)
                {
                    var packet = BrlApiProtocol.ReadPacket(stream);
                    if (packet is null)
                        break;
                    switch (packet.Type)
                    {
                        case BrlApiPacketType.Key:
                            var args = BrlApiKeyCodes.Decode(BrlApiProtocol.DecodeKey(packet.Payload));
                            if (args is not null)
                                owner.RaiseKey(args);
                            break;
                        case BrlApiPacketType.Error:
                            owner._logger?.LogWarning("BRLTTY error: {Error}",
                                BrlApiProtocol.ErrorName(BrlApiProtocol.DecodeError(packet.Payload)));
                            break;
                        case BrlApiPacketType.Exception:
                            var (code, type) = BrlApiProtocol.DecodeException(packet.Payload);
                            owner._logger?.LogWarning("BRLTTY exception on a {Packet} packet: {Error}",
                                BrlApiProtocol.Describe(type), BrlApiProtocol.ErrorName(code));
                            break;
                        // ACKs (of leaving tty mode) and anything else need nothing.
                    }
                }
            }
            catch (Exception ex) when (!IsLost)
            {
                owner._logger?.LogDebug("BRLTTY connection read failed: {Message}", ex.Message);
            }
            catch
            {
                // Closed on purpose.
            }
            Lose();
        }

        public void Write(ReadOnlySpan<byte> cells)
        {
            lock (owner._writeLock)
            {
                if (IsLost)
                    return;
                var line = new byte[CellCount];
                cells[..Math.Min(cells.Length, line.Length)].CopyTo(line);

                int first = 0, last = line.Length - 1;
                if (_shown is not null)
                {
                    while (first < line.Length && line[first] == _shown[first])
                        first++;
                    if (first == line.Length)
                        return; // nothing changed
                    while (line[last] == _shown[last])
                        last--;
                }
                _shown = line;
                SendLocked(BrlApiProtocol.EncodeWriteDots(first + 1, line.AsSpan(first, last - first + 1)));
            }
        }

        public void Send(byte[] packet)
        {
            lock (owner._writeLock)
                SendLocked(packet);
        }

        private void SendLocked(byte[] packet)
        {
            if (IsLost)
                return;
            try
            {
                stream.Write(packet);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
            {
                owner._logger?.LogDebug("BRLTTY connection write failed: {Message}", ex.Message);
                Lose();
            }
        }

        /// <summary>Marks the connection lost, closes it and raises Disconnected (once).</summary>
        private void Lose()
        {
            if (Interlocked.Exchange(ref _lost, 1) != 0)
                return;
            client.Dispose();
            owner.RaiseDisconnected();
        }

        /// <summary>Closes the connection on purpose, without raising Disconnected.</summary>
        public void Close()
        {
            if (Interlocked.Exchange(ref _lost, 1) != 0)
                return;
            client.Dispose();
        }
    }
}
