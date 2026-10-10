using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vox.Core.Updates;

/// <summary>Which releases to be told about.</summary>
public enum UpdateChannel
{
    /// <summary>Never check.</summary>
    Off,
    /// <summary>Releases only.</summary>
    Stable,
    /// <summary>Releases and betas.</summary>
    Beta,
}

/// <summary>A release in the update feed: its version, channel, installer address and checksum.</summary>
public sealed record UpdateRelease(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("notes")] string? Notes = null);

/// <summary>
/// The update feed: a JSON list of releases, published with a detached signature (feed.json.sig,
/// base64 RSA-SHA256 over the feed's bytes) made with Vox's release key. Vox trusts a feed only
/// when the signature checks out against the public key it ships with, and an installer only
/// when its SHA-256 is the one that signed feed gives.
/// </summary>
public static class UpdateFeed
{
    private sealed record FeedFile([property: JsonPropertyName("releases")] List<UpdateRelease>? Releases);

    /// <summary>The releases in a feed; throws <see cref="FormatException"/> on a malformed one.</summary>
    public static IReadOnlyList<UpdateRelease> Parse(string json)
    {
        FeedFile? feed;
        try { feed = JsonSerializer.Deserialize<FeedFile>(json); }
        catch (JsonException ex) { throw new FormatException("The update feed isn't valid JSON.", ex); }
        var releases = feed?.Releases ?? throw new FormatException("The update feed has no releases list.");
        foreach (var release in releases)
        {
            if (!System.Version.TryParse(release.Version, out _) || string.IsNullOrWhiteSpace(release.Url)
                || release.Sha256 is not { Length: 64 } || !Uri.TryCreate(release.Url, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps)
                throw new FormatException($"Update feed entry {release.Version} is incomplete or not served over HTTPS.");
        }
        return releases;
    }

    /// <summary>True when <paramref name="signature"/> is the release key's signature of <paramref name="feed"/>.</summary>
    public static bool IsSigned(byte[] feed, string signature, string publicKeyPem)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa.VerifyData(feed, Convert.FromBase64String(signature.Trim()), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// The newest release newer than <paramref name="current"/> on <paramref name="channel"/>
    /// (the beta channel includes stable releases), or null.
    /// </summary>
    public static UpdateRelease? Newer(IEnumerable<UpdateRelease> releases, Version current, UpdateChannel channel)
    {
        if (channel == UpdateChannel.Off)
            return null;
        return releases
            .Where(r => channel == UpdateChannel.Beta || r.Channel.Equals("stable", StringComparison.OrdinalIgnoreCase))
            .Select(r => (Release: r, Version: System.Version.Parse(r.Version)))
            .Where(r => r.Version > current)
            .OrderByDescending(r => r.Version)
            .Select(r => r.Release)
            .FirstOrDefault();
    }

    /// <summary>True when <paramref name="file"/>'s SHA-256 is <paramref name="expected"/> (hex, any case).</summary>
    public static bool HasChecksum(Stream file, string expected)
    {
        var hash = SHA256.HashData(file);
        return Convert.ToHexString(hash).Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
