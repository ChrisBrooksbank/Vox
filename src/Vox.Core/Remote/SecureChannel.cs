using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Vox.Core.Remote;

/// <summary>Which end of a remote session this is.</summary>
public enum RemoteRole : byte
{
    /// <summary>The computer being controlled: it receives keys and sends what it says.</summary>
    Controlled = 1,
    /// <summary>The computer controlling the other one.</summary>
    Controlling = 2,
}

/// <summary>The other side doesn't know the session code (or isn't Vox).</summary>
public sealed class RemoteAuthenticationException(string message) : Exception(message);

/// <summary>
/// An encrypted, authenticated connection between two Vox computers over any stream (a direct TCP
/// connection or one through a relay). The handshake is an ephemeral ECDH (P-256) exchange whose
/// secret is mixed with a key derived from the session code (PBKDF2-SHA256, 100,000 rounds, salted
/// with both sides' random nonces) and the handshake transcript (HKDF-SHA256), so only someone who
/// knows the code gets the keys, and a recorded session can't be decrypted later even with the
/// code. Each side then proves it has the keys with an encrypted confirmation. Messages are
/// AES-256-GCM frames with a key per direction and a counter nonce that must go up by one, so frames
/// can't be changed, replayed, reordered or reflected.
/// </summary>
public sealed class SecureChannel : IAsyncDisposable
{
    private static readonly byte[] Magic = "VOXR"u8.ToArray();
    private const byte ProtocolVersion = 1;
    private const int NonceLength = 32;
    private const int TagLength = 16;
    private const int MaxFrame = 1024 * 1024;
    private const int Pbkdf2Rounds = 100_000;

    private readonly Stream _stream;
    private readonly AesGcm _send;
    private readonly AesGcm _receive;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ulong _sendCounter;
    private ulong _receiveCounter;

    private SecureChannel(Stream stream, byte[] sendKey, byte[] receiveKey)
    {
        _stream = stream;
        _send = new AesGcm(sendKey, TagLength);
        _receive = new AesGcm(receiveKey, TagLength);
        CryptographicOperations.ZeroMemory(sendKey);
        CryptographicOperations.ZeroMemory(receiveKey);
    }

    /// <summary>
    /// Runs the handshake as <paramref name="role"/> with <paramref name="code"/>; throws
    /// <see cref="RemoteAuthenticationException"/> when the other side has another code or the same role.
    /// </summary>
    public static async Task<SecureChannel> EstablishAsync(Stream stream, RemoteRole role, string code, CancellationToken cancellationToken)
    {
        var normal = RemoteCode.Normalize(code) ?? throw new ArgumentException("Not a session code", nameof(code));
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = ecdh.PublicKey.ExportSubjectPublicKeyInfo();
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);

        // Hello: magic, version, role, nonce, public key
        var hello = new byte[Magic.Length + 2 + NonceLength + 2 + publicKey.Length];
        Magic.CopyTo(hello, 0);
        hello[4] = ProtocolVersion;
        hello[5] = (byte)role;
        nonce.CopyTo(hello, 6);
        BinaryPrimitives.WriteUInt16BigEndian(hello.AsSpan(6 + NonceLength), (ushort)publicKey.Length);
        publicKey.CopyTo(hello, 8 + NonceLength);
        await stream.WriteAsync(hello, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        var head = new byte[Magic.Length + 2 + NonceLength + 2];
        await stream.ReadExactlyAsync(head, cancellationToken).ConfigureAwait(false);
        if (!head.AsSpan(0, 4).SequenceEqual(Magic) || head[4] != ProtocolVersion)
            throw new RemoteAuthenticationException("The other computer isn't running a compatible Vox.");
        var peerRole = (RemoteRole)head[5];
        if (peerRole == role || !Enum.IsDefined(peerRole))
            throw new RemoteAuthenticationException("Both computers are trying to be the same side of the session.");
        var peerNonce = head.AsSpan(6, NonceLength).ToArray();
        var keyLength = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(6 + NonceLength));
        if (keyLength is 0 or > 512)
            throw new RemoteAuthenticationException("The other computer sent an invalid key.");
        var peerKey = new byte[keyLength];
        await stream.ReadExactlyAsync(peerKey, cancellationToken).ConfigureAwait(false);
        var peerHello = new byte[head.Length + keyLength];
        head.CopyTo(peerHello, 0);
        peerKey.CopyTo(peerHello, head.Length);

        byte[] shared;
        try
        {
            using var peer = ECDiffieHellman.Create();
            peer.ImportSubjectPublicKeyInfo(peerKey, out _);
            shared = ecdh.DeriveRawSecretAgreement(peer.PublicKey);
        }
        catch (CryptographicException)
        {
            throw new RemoteAuthenticationException("The other computer sent an invalid key.");
        }

        // Everything is ordered controlled side first, so both sides compute the same
        var (controlledHello, controllingHello) = role == RemoteRole.Controlled ? (hello, peerHello) : (peerHello, hello);
        var (controlledNonce, controllingNonce) = role == RemoteRole.Controlled ? (nonce, peerNonce) : (peerNonce, nonce);
        byte[] salt = [.. "vox-remote-code-v1"u8, .. controlledNonce, .. controllingNonce];
        var codeKey = Rfc2898DeriveBytes.Pbkdf2(normal, salt,
            Pbkdf2Rounds, HashAlgorithmName.SHA256, 32);
        var transcript = SHA256.HashData([.. controlledHello, .. controllingHello]);
        var secret = HKDF.Extract(HashAlgorithmName.SHA256, [.. shared, .. codeKey], transcript);
        var toControlling = HKDF.Expand(HashAlgorithmName.SHA256, secret, 32, "vox remote v1 controlled to controlling"u8.ToArray());
        var toControlled = HKDF.Expand(HashAlgorithmName.SHA256, secret, 32, "vox remote v1 controlling to controlled"u8.ToArray());
        CryptographicOperations.ZeroMemory(shared);
        CryptographicOperations.ZeroMemory(codeKey);
        CryptographicOperations.ZeroMemory(secret);

        var channel = role == RemoteRole.Controlled
            ? new SecureChannel(stream, toControlling, toControlled)
            : new SecureChannel(stream, toControlled, toControlling);

        // Each side proves it derived the same keys: a frame only the right keys open
        byte[] confirm = [(byte)role, .. "confirm"u8];
        await channel.SendAsync(confirm, cancellationToken).ConfigureAwait(false);
        byte[]? confirmation;
        try
        {
            confirmation = await channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (CryptographicException)
        {
            await channel.DisposeAsync().ConfigureAwait(false);
            throw new RemoteAuthenticationException("The session code doesn't match.");
        }
        if (confirmation is null || confirmation.Length != 8 || confirmation[0] != (byte)peerRole || !confirmation.AsSpan(1).SequenceEqual("confirm"u8))
        {
            await channel.DisposeAsync().ConfigureAwait(false);
            throw new RemoteAuthenticationException("The session code doesn't match.");
        }
        return channel;
    }

    /// <summary>Encrypts and sends one message (thread-safe).</summary>
    public async Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        if (message.Length > MaxFrame - 12 - TagLength)
            throw new ArgumentException("Message too large", nameof(message));
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var nonce = new byte[12];
            BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), ++_sendCounter);
            var frame = new byte[4 + 12 + message.Length + TagLength];
            BinaryPrimitives.WriteInt32BigEndian(frame, 12 + message.Length + TagLength);
            nonce.CopyTo(frame, 4);
            _send.Encrypt(nonce, message.Span, frame.AsSpan(16, message.Length), frame.AsSpan(16 + message.Length, TagLength));
            await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// The next message, or null when the other side closed the connection. Throws
    /// <see cref="CryptographicException"/> for a frame that was changed, replayed or out of order.
    /// One reader at a time.
    /// </summary>
    public async Task<byte[]?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var header = new byte[4];
        try
        {
            await _stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException)
        {
            return null;
        }
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 12 + TagLength || length > MaxFrame)
            throw new CryptographicException("Invalid frame length");
        var frame = new byte[length];
        await _stream.ReadExactlyAsync(frame, cancellationToken).ConfigureAwait(false);
        var counter = BinaryPrimitives.ReadUInt64BigEndian(frame.AsSpan(4, 8));
        if (counter != _receiveCounter + 1 || BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(0, 4)) != 0)
            throw new CryptographicException("Frame out of order");
        var plain = new byte[length - 12 - TagLength];
        _receive.Decrypt(frame.AsSpan(0, 12), frame.AsSpan(12, plain.Length), frame.AsSpan(12 + plain.Length, TagLength), plain);
        _receiveCounter = counter;
        return plain;
    }

    public async ValueTask DisposeAsync()
    {
        _send.Dispose();
        _receive.Dispose();
        _sendLock.Dispose();
        await _stream.DisposeAsync().ConfigureAwait(false);
    }
}
