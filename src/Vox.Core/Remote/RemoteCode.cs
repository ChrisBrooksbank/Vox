using System.Security.Cryptography;

namespace Vox.Core.Remote;

/// <summary>
/// A session code: 12 characters of Crockford's base32 (60 random bits), shown and said as
/// XXXX-XXXX-XXXX. The person whose computer is controlled reads it to the one controlling it. It
/// is never sent anywhere: both sides prove they know it (<see cref="SecureChannel"/>), and a relay
/// server only sees a slow one-way hash of it (<see cref="RelayChannel"/>).
/// </summary>
public static class RemoteCode
{
    public const int Length = 12;
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>A new random code.</summary>
    public static string New()
    {
        var chars = new char[Length];
        for (int i = 0; i < Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return Format(new string(chars));
    }

    /// <summary>The code as people read it: XXXX-XXXX-XXXX.</summary>
    public static string Format(string code)
    {
        var normal = Normalize(code) ?? code;
        return normal.Length == Length ? $"{normal[..4]}-{normal[4..8]}-{normal[8..]}" : normal;
    }

    /// <summary>
    /// The code as typed, normalized: upper case, without spaces and dashes, with the letters people
    /// confuse read as the digits they look like (O as 0, I and L as 1); null when it isn't a code.
    /// </summary>
    public static string? Normalize(string? typed)
    {
        if (typed is null)
            return null;
        var chars = new List<char>(Length);
        foreach (var c in typed.ToUpperInvariant())
        {
            if (c is ' ' or '-' or '\t')
                continue;
            var mapped = c switch { 'O' => '0', 'I' or 'L' => '1', _ => c };
            if (!Alphabet.Contains(mapped))
                return null;
            chars.Add(mapped);
        }
        return chars.Count == Length ? new string([.. chars]) : null;
    }

    /// <summary>The code spelled out for speech: "A 7 K 2, ...".</summary>
    public static string Spoken(string code)
    {
        var formatted = Format(code);
        return string.Join(", ", formatted.Split('-').Select(group => string.Join(' ', group.ToCharArray())));
    }

    /// <summary>
    /// The channel a relay server pairs the two sides on: a slow hash of the code (PBKDF2, 100,000
    /// rounds), so the relay can't work the code out from it in any reasonable time.
    /// </summary>
    public static string RelayChannel(string code)
    {
        var normal = Normalize(code) ?? throw new ArgumentException("Not a session code", nameof(code));
        var bytes = Rfc2898DeriveBytes.Pbkdf2(normal, "vox-remote-relay-channel-v1"u8.ToArray(), 100_000, HashAlgorithmName.SHA256, 16);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
