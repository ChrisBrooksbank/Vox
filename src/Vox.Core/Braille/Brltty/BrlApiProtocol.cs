using System.Buffers.Binary;
using System.Text;

namespace Vox.Core.Braille.Brltty;

/// <summary>BrlAPI packet types (<c>BRLAPI_PACKET_*</c> in brlapi_protocol.h): one ASCII character each.</summary>
public enum BrlApiPacketType : uint
{
    Version = 'v',
    Auth = 'a',
    GetDriverName = 'n',
    GetModelId = 'd',
    GetDisplaySize = 's',
    EnterTtyMode = 't',
    SetFocus = 'F',
    LeaveTtyMode = 'L',
    Key = 'k',
    IgnoreKeyRanges = 'm',
    AcceptKeyRanges = 'u',
    Write = 'w',
    EnterRawMode = '*',
    LeaveRawMode = '#',
    Packet = 'p',
    Ack = 'A',
    Error = 'e',
    Exception = 'E',
    SuspendDriver = 'S',
    ResumeDriver = 'R',
    Synchronize = 'Z',
}

/// <summary>A BrlAPI packet: its type and payload (the 8-byte header not included).</summary>
public sealed record BrlApiPacket(BrlApiPacketType Type, byte[] Payload);

/// <summary>A malformed or unexpected BrlAPI packet, or an error reported by the server.</summary>
public sealed class BrlApiProtocolException(string message) : IOException(message);

/// <summary>
/// The BrlAPI wire protocol (version 8, BRLTTY 6.x), spoken directly over TCP so no native
/// brlapi library is needed. Every packet is a header of two big-endian <c>uint32</c>s, the
/// payload size and the packet type, followed by the payload; all integers are big-endian.
/// The encoders and decoders here are pure; <see cref="ReadPacket"/> only frames a stream.
/// </summary>
public static class BrlApiProtocol
{
    /// <summary><c>BRLAPI_PROTOCOL_VERSION</c>.</summary>
    public const uint ProtocolVersion = 8;

    /// <summary><c>BRLAPI_MAXPACKETSIZE</c>: the largest payload either side sends.</summary>
    public const int MaxPacketSize = 4096;

    /// <summary>The size of a packet header (payload size, type).</summary>
    public const int HeaderSize = 8;

    /// <summary><c>BRLAPI_SOCKETPORTNUM</c>: the TCP port of display <c>:0</c>; display <c>:N</c> listens on 4101 + N.</summary>
    public const int DefaultPort = 4101;

    /// <summary><c>BRLAPI_AUTH_NONE</c>: the server needs no authentication.</summary>
    public const uint AuthNone = 'N';

    /// <summary><c>BRLAPI_AUTH_KEY</c>: the client sends the contents of the shared key file (brlapi.key).</summary>
    public const uint AuthKey = 'K';

    /// <summary><c>BRLAPI_AUTH_CRED</c>: the server checks the peer's credentials (local sockets only).</summary>
    public const uint AuthCred = 'C';

    /// <summary><c>BRLAPI_CURSOR_OFF</c>: no cursor drawn by the server.</summary>
    public const int CursorOff = 0;

    /// <summary><c>BRLAPI_TTY_DEFAULT</c>: let the client library guess the tty (never sent as is).</summary>
    public const int TtyDefault = -1;

    /// <summary>Which optional fields a WRITE packet carries (<c>BRLAPI_WF_*</c>), in the order they follow the flags.</summary>
    [Flags]
    public enum WriteFlags : uint
    {
        None = 0,
        DisplayNumber = 0x01,
        Region = 0x02,
        Text = 0x04,
        AttrAnd = 0x08,
        AttrOr = 0x10,
        Cursor = 0x20,
        Charset = 0x40,
    }

    /// <summary>The BrlAPI error codes (<c>BRLAPI_ERROR_*</c>), for log messages.</summary>
    private static readonly string[] ErrorNames =
    [
        "success", "out of memory", "tty busy", "device busy", "unknown instruction",
        "illegal instruction", "invalid parameter", "invalid packet", "connection refused",
        "operation not supported", "getaddrinfo error", "libc error", "unknown tty",
        "protocol version mismatch", "end of file", "empty key", "driver error", "authentication failed",
    ];

    /// <summary>A readable name for a BrlAPI error code.</summary>
    public static string ErrorName(uint code) =>
        code < ErrorNames.Length ? ErrorNames[code] : $"error {code}";

    /// <summary>
    /// The TCP host and port of a BrlAPI host string as brlapi reads it: <c>host:N</c> is port
    /// 4101 + N, <c>:N</c> is that port on the local machine, a bare host is display 0, and an
    /// empty or null string is <c>127.0.0.1:4101</c>.
    /// </summary>
    public static (string Host, int Port) ParseHost(string? hostSpec)
    {
        if (string.IsNullOrWhiteSpace(hostSpec))
            return ("127.0.0.1", DefaultPort);
        var spec = hostSpec.Trim();
        var colon = spec.LastIndexOf(':');
        var host = colon < 0 ? spec : spec[..colon];
        var display = 0;
        if (colon >= 0 && (!int.TryParse(spec[(colon + 1)..], out display) || display < 0 || display > 1000))
            throw new FormatException($"'{hostSpec}' is not a BrlAPI host (host:display).");
        if (host.Length == 0 || host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            host = "127.0.0.1";
        return (host, DefaultPort + display);
    }

    // ---- Framing -------------------------------------------------------------------------------

    /// <summary>A whole packet (header and payload) ready to send.</summary>
    public static byte[] Encode(BrlApiPacketType type, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPacketSize)
            throw new ArgumentException($"BrlAPI payloads are at most {MaxPacketSize} bytes.", nameof(payload));
        var packet = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(packet, (uint)payload.Length);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), (uint)type);
        payload.CopyTo(packet.AsSpan(HeaderSize));
        return packet;
    }

    /// <summary>Reads the payload size and type from a packet header.</summary>
    public static (int Size, BrlApiPacketType Type) DecodeHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderSize)
            throw new BrlApiProtocolException("Truncated BrlAPI packet header.");
        var size = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (size > MaxPacketSize)
            throw new BrlApiProtocolException($"BrlAPI packet of {size} bytes is too large.");
        return ((int)size, (BrlApiPacketType)BinaryPrimitives.ReadUInt32BigEndian(header[4..]));
    }

    /// <summary>
    /// Reads the next packet from <paramref name="stream"/>, blocking until it has arrived.
    /// Null when the stream ends cleanly before a header; a stream that ends inside a packet throws.
    /// </summary>
    public static BrlApiPacket? ReadPacket(Stream stream)
    {
        var header = new byte[HeaderSize];
        if (!ReadExactly(stream, header, allowEmpty: true))
            return null;
        var (size, type) = DecodeHeader(header);
        var payload = new byte[size];
        if (size > 0)
            ReadExactly(stream, payload, allowEmpty: false);
        return new BrlApiPacket(type, payload);
    }

    private static bool ReadExactly(Stream stream, byte[] buffer, bool allowEmpty)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n == 0)
            {
                if (read == 0 && allowEmpty)
                    return false;
                throw new EndOfStreamException("The BrlAPI connection closed inside a packet.");
            }
            read += n;
        }
        return true;
    }

    // ---- Client packets ------------------------------------------------------------------------

    /// <summary>VERSION: the protocol version the client speaks.</summary>
    public static byte[] EncodeVersion(uint version = ProtocolVersion) =>
        Encode(BrlApiPacketType.Version, U32(version));

    /// <summary>AUTH with the key method: the type, then the key file's bytes as they are.</summary>
    public static byte[] EncodeAuthKey(ReadOnlySpan<byte> key)
    {
        var payload = new byte[4 + key.Length];
        BinaryPrimitives.WriteUInt32BigEndian(payload, AuthKey);
        key.CopyTo(payload.AsSpan(4));
        return Encode(BrlApiPacketType.Auth, payload);
    }

    /// <summary>AUTH with a method that carries no data (credentials).</summary>
    public static byte[] EncodeAuth(uint method) => Encode(BrlApiPacketType.Auth, U32(method));

    /// <summary>A request with no payload (GETDISPLAYSIZE, GETDRIVERNAME, LEAVETTYMODE, ...).</summary>
    public static byte[] EncodeEmpty(BrlApiPacketType type) => Encode(type, ReadOnlySpan<byte>.Empty);

    /// <summary>
    /// ENTERTTYMODE: the number of ttys, the tty path, then the driver name as a length byte and
    /// its characters. An empty path takes the root of BRLTTY's tty tree (what brlapi's
    /// <c>enterTtyModeWithPath</c> sends on Windows, where there is no WINDOWPATH); an empty driver
    /// name asks for keys as BrlAPI command codes rather than the driver's raw key codes.
    /// </summary>
    public static byte[] EncodeEnterTtyMode(IReadOnlyList<int> ttyPath, string driverName = "")
    {
        var name = Encoding.ASCII.GetBytes(driverName);
        if (name.Length > 255)
            throw new ArgumentException("Driver names are at most 255 characters.", nameof(driverName));
        var payload = new byte[4 + 4 * ttyPath.Count + 1 + name.Length];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)ttyPath.Count);
        for (var i = 0; i < ttyPath.Count; i++)
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4 + 4 * i), ttyPath[i]);
        var p = 4 + 4 * ttyPath.Count;
        payload[p] = (byte)name.Length;
        name.CopyTo(payload, p + 1);
        return Encode(BrlApiPacketType.EnterTtyMode, payload);
    }

    /// <summary>
    /// WRITE of raw dots, as brlapi's <c>writeDots</c> does it: a region starting at cell
    /// <paramref name="regionBegin"/> (1-based, as BrlAPI counts), a text of blanks, an AND mask
    /// of zeros (clearing the blanks' dots) and the cells as the OR mask, and the cursor
    /// (<see cref="CursorOff"/>, or a 1-based cell).
    /// </summary>
    public static byte[] EncodeWriteDots(int regionBegin, ReadOnlySpan<byte> cells, int cursor = CursorOff)
    {
        if (regionBegin < 1)
            throw new ArgumentOutOfRangeException(nameof(regionBegin), "BrlAPI regions start at 1.");
        var n = cells.Length;
        var flags = WriteFlags.Region | WriteFlags.Text | WriteFlags.AttrAnd | WriteFlags.AttrOr | WriteFlags.Cursor;
        var payload = new byte[4 + 8 + 4 + n + n + n + 4];
        var p = 0;
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(p), (uint)flags); p += 4;
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(p), regionBegin); p += 4;
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(p), n); p += 4;
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(p), (uint)n); p += 4;
        payload.AsSpan(p, n).Fill((byte)' '); p += n;
        p += n; // AND mask: zeros
        cells.CopyTo(payload.AsSpan(p)); p += n;
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(p), cursor);
        return Encode(BrlApiPacketType.Write, payload);
    }

    // ---- Server packets ------------------------------------------------------------------------

    /// <summary>The protocol version in a VERSION packet.</summary>
    public static uint DecodeVersion(ReadOnlySpan<byte> payload) => ReadU32(payload, 0, "VERSION");

    /// <summary>The authentication methods a server's AUTH packet offers, in its order of preference.</summary>
    public static IReadOnlyList<uint> DecodeAuthMethods(ReadOnlySpan<byte> payload)
    {
        var methods = new uint[payload.Length / 4];
        for (var i = 0; i < methods.Length; i++)
            methods[i] = BinaryPrimitives.ReadUInt32BigEndian(payload[(4 * i)..]);
        return methods;
    }

    /// <summary>The columns and rows of a GETDISPLAYSIZE reply.</summary>
    public static (int Columns, int Rows) DecodeDisplaySize(ReadOnlySpan<byte> payload) =>
        ((int)ReadU32(payload, 0, "GETDISPLAYSIZE"), (int)ReadU32(payload, 4, "GETDISPLAYSIZE"));

    /// <summary>The 64-bit key code of a KEY packet (high word first).</summary>
    public static ulong DecodeKey(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
            throw new BrlApiProtocolException("Truncated BrlAPI KEY packet.");
        return BinaryPrimitives.ReadUInt64BigEndian(payload);
    }

    /// <summary>The error code of an ERROR packet.</summary>
    public static uint DecodeError(ReadOnlySpan<byte> payload) => ReadU32(payload, 0, "ERROR");

    /// <summary>The error code and the type of the offending packet in an EXCEPTION packet.</summary>
    public static (uint Code, BrlApiPacketType Type) DecodeException(ReadOnlySpan<byte> payload) =>
        (ReadU32(payload, 0, "EXCEPTION"), (BrlApiPacketType)ReadU32(payload, 4, "EXCEPTION"));

    /// <summary>A NUL-terminated (or unterminated) string reply, such as GETDRIVERNAME's.</summary>
    public static string DecodeString(ReadOnlySpan<byte> payload)
    {
        var end = payload.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? payload : payload[..end]);
    }

    /// <summary>A readable description of a packet type, such as <c>'w'</c>, for logs.</summary>
    public static string Describe(BrlApiPacketType type) =>
        Enum.IsDefined(type) ? type.ToString() : $"0x{(uint)type:X}";

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static uint ReadU32(ReadOnlySpan<byte> payload, int offset, string packet)
    {
        if (payload.Length < offset + 4)
            throw new BrlApiProtocolException($"Truncated BrlAPI {packet} packet.");
        return BinaryPrimitives.ReadUInt32BigEndian(payload[offset..]);
    }
}
