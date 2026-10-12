using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Vox.Core.Ai;

/// <summary>Keeps the cloud provider's API key. Never logged, never in settings.json.</summary>
public interface IAiKeyStore
{
    /// <summary>A key is kept.</summary>
    bool HasKey { get; }

    /// <summary>The key, or null when none is kept (or it can't be read).</summary>
    string? Read();

    /// <summary>Keeps <paramref name="key"/>; null or blank forgets it. False when it couldn't be saved.</summary>
    bool Save(string? key);
}

/// <summary>
/// The key encrypted for the Windows user (DPAPI, CurrentUser) in <c>ai-key.dat</c> beside the
/// settings: another account on the computer, or the file copied elsewhere, can't read it. Not
/// used in secure mode (no user profile there).
/// </summary>
public sealed class ProtectedAiKeyStore(string? path, ILogger<ProtectedAiKeyStore> logger) : IAiKeyStore
{
    // Ties the encrypted key to Vox: another program's DPAPI blob can't be passed off as it
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Vox AI provider key");

    /// <summary>The default place: <c>ai-key.dat</c> in Vox's user data folder.</summary>
    public static string DefaultPath => Path.Combine(Lifecycle.VoxPaths.UserData, "ai-key.dat");

    private string? _cached;

    public bool HasKey => path is not null && (_cached is not null || File.Exists(path));

    public string? Read()
    {
        if (_cached is not null || path is null || !File.Exists(path))
            return _cached;
        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            _cached = Encoding.UTF8.GetString(bytes);
            return _cached;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or PlatformNotSupportedException)
        {
            logger.LogWarning("Could not read the AI provider key: {Error}", ex.GetType().Name);
            return null;
        }
    }

    public bool Save(string? key)
    {
        if (path is null)
            return false;
        try
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                File.Delete(path);
                _cached = null;
                return true;
            }
            var trimmed = key.Trim();
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(trimmed), Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            _cached = trimmed;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or PlatformNotSupportedException)
        {
            logger.LogWarning("Could not save the AI provider key: {Error}", ex.GetType().Name);
            return false;
        }
    }
}
