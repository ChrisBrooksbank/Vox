using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Vox.Core.AddOns;

/// <summary>How far an add-on package can be trusted.</summary>
public enum AddOnTrust
{
    /// <summary>No signature: anyone could have made or changed it.</summary>
    Unsigned,
    /// <summary>Signed, by a key Vox doesn't know: unchanged since its author signed it, but who that is isn't known.</summary>
    UnknownPublisher,
    /// <summary>Signed by a key Vox trusts (Vox's own, or one the user added).</summary>
    TrustedPublisher,
    /// <summary>Its checksum is the one in Vox's signed add-on catalogue.</summary>
    Catalogue,
}

/// <summary>A checked package: its manifest, how far it can be trusted, and its SHA-256.</summary>
/// <param name="Publisher">The signing key's fingerprint, or null when unsigned.</param>
public sealed record AddOnPackageInfo(AddOnManifest Manifest, AddOnTrust Trust, string? Publisher, string Sha256);

/// <summary>A package that is damaged, tampered with or not an add-on.</summary>
public sealed class AddOnPackageException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// An add-on package (.voxaddon): a zip with addon.json (the manifest, listing every other file
/// with its SHA-256), the files, and optionally addon.sig (base64 RSA-SHA256 signature of
/// addon.json's bytes) with addon.pem (the signer's public key). Reading checks everything before
/// anything is extracted, and extracting checks every file again as it is written.
/// </summary>
public static class AddOnPackage
{
    public const string Extension = ".voxaddon";
    public const string SignatureFileName = "addon.sig";
    public const string KeyFileName = "addon.pem";

    /// <summary>Limits against zip bombs.</summary>
    public const long MaxTotalBytes = 200L * 1024 * 1024;
    public const int MaxFiles = 2000;
    private const long MaxManifestBytes = 1024 * 1024;

    /// <summary>Reads and checks the package at <paramref name="path"/>; throws <see cref="AddOnPackageException"/>.</summary>
    public static AddOnPackageInfo Read(string path, AddOnTrustStore trust)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var sha256 = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
            file.Position = 0;
            using var zip = new ZipArchive(file, ZipArchiveMode.Read);
            var (manifest, manifestBytes) = ReadManifest(zip);
            CheckEntries(zip, manifest);
            foreach (var (name, hash) in manifest.Files)
            {
                using var stream = Entry(zip, name).Open();
                if (!HashMatches(stream, hash))
                    throw new AddOnPackageException($"The file '{name}' isn't the one the manifest lists: the package is damaged or was changed.");
            }
            var (publisher, signedTrust) = CheckSignature(zip, manifestBytes, trust);
            return new AddOnPackageInfo(manifest, signedTrust, publisher, sha256);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or FormatException)
        {
            throw new AddOnPackageException(ex is FormatException ? ex.Message : "The file isn't an add-on package, or can't be read.", ex);
        }
    }

    /// <summary>
    /// Writes the package's files into <paramref name="destination"/> (which must not exist),
    /// checking each against <paramref name="expected"/> (the manifest <see cref="Read"/> returned)
    /// as it is written. On any failure nothing is left behind.
    /// </summary>
    public static void Extract(string path, AddOnManifest expected, string destination)
    {
        if (Directory.Exists(destination))
            throw new IOException($"{destination} exists already.");
        Directory.CreateDirectory(destination);
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var zip = new ZipArchive(file, ZipArchiveMode.Read);
            var (manifest, _) = ReadManifest(zip);
            // The package changed since it was checked
            if (manifest.ToJson() != expected.ToJson())
                throw new AddOnPackageException("The package changed while it was being installed.");
            CheckEntries(zip, manifest);
            var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/'))
                    continue;
                var target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new AddOnPackageException($"The file path '{entry.FullName}' isn't allowed.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using (var input = entry.Open())
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                    input.CopyTo(output);
                if (manifest.Files.TryGetValue(entry.FullName, out var hash))
                {
                    using var written = File.OpenRead(target);
                    if (!HashMatches(written, hash))
                        throw new AddOnPackageException($"The file '{entry.FullName}' isn't the one the manifest lists.");
                }
            }
        }
        catch (Exception ex) when (ex is not AddOnPackageException)
        {
            TryDelete(destination);
            throw ex is InvalidDataException or FormatException ? new AddOnPackageException("The package can't be read.", ex) : ex;
        }
        catch
        {
            TryDelete(destination);
            throw;
        }
    }

    /// <summary>
    /// Makes a package of <paramref name="sourceDirectory"/>: its addon.json (whose "files" list is
    /// filled in from the folder's other files) and every other file, signed with
    /// <paramref name="privateKeyPem"/> (an RSA key) when given. Returns the manifest written.
    /// </summary>
    public static AddOnManifest Create(string sourceDirectory, string outputPath, string? privateKeyPem = null)
    {
        var manifestPath = Path.Combine(sourceDirectory, AddOnManifest.FileName);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"No {AddOnManifest.FileName} in {sourceDirectory}.", manifestPath);
        var files = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(sourceDirectory, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !AddOnManifest.IsReserved(f))
            .Order(StringComparer.Ordinal)
            .ToList();
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in files)
        {
            using var stream = File.OpenRead(Path.Combine(sourceDirectory, name));
            hashes[name] = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        var template = System.Text.Json.JsonSerializer.Deserialize<AddOnManifest>(File.ReadAllText(manifestPath))
            ?? throw new FormatException("addon.json is empty.");
        var manifest = template with { Files = hashes };
        var problems = manifest.Problems();
        if (problems.Count > 0)
            throw new FormatException(string.Join(" ", problems));
        var manifestBytes = Encoding.UTF8.GetBytes(manifest.ToJson());

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        if (File.Exists(outputPath))
            File.Delete(outputPath);
        using var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        Write(zip, AddOnManifest.FileName, manifestBytes);
        if (privateKeyPem is not null)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
            var signature = rsa.SignData(manifestBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Write(zip, SignatureFileName, Encoding.ASCII.GetBytes(Convert.ToBase64String(signature)));
            Write(zip, KeyFileName, Encoding.ASCII.GetBytes(rsa.ExportSubjectPublicKeyInfoPem()));
        }
        foreach (var name in files)
            zip.CreateEntryFromFile(Path.Combine(sourceDirectory, name), name, CompressionLevel.Optimal);
        return manifest;
    }

    /// <summary>The signer and trust of an installed add-on's folder (its addon.json, .sig and .pem).</summary>
    public static (string? Publisher, AddOnTrust Trust) TrustOfFolder(string directory, AddOnTrustStore trust)
    {
        try
        {
            var manifest = File.ReadAllBytes(Path.Combine(directory, AddOnManifest.FileName));
            var signaturePath = Path.Combine(directory, SignatureFileName);
            var keyPath = Path.Combine(directory, KeyFileName);
            if (!File.Exists(signaturePath) || !File.Exists(keyPath))
                return (null, AddOnTrust.Unsigned);
            return Verify(manifest, File.ReadAllText(signaturePath), File.ReadAllText(keyPath), trust);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AddOnPackageException)
        {
            return (null, AddOnTrust.Unsigned);
        }
    }

    private static (AddOnManifest Manifest, byte[] Bytes) ReadManifest(ZipArchive zip)
    {
        var entry = zip.GetEntry(AddOnManifest.FileName)
            ?? throw new AddOnPackageException("The package has no addon.json: it isn't a Vox add-on.");
        if (entry.Length > MaxManifestBytes)
            throw new AddOnPackageException("The package's addon.json is too large.");
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        return (AddOnManifest.Parse(Encoding.UTF8.GetString(bytes)), bytes);
    }

    private static void CheckEntries(ZipArchive zip, AddOnManifest manifest)
    {
        if (zip.Entries.Count > MaxFiles)
            throw new AddOnPackageException("The package has too many files.");
        long total = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/'))
                continue;
            total += entry.Length;
            if (total > MaxTotalBytes)
                throw new AddOnPackageException("The package is too large.");
            if (!seen.Add(entry.FullName))
                throw new AddOnPackageException($"The package has '{entry.FullName}' twice.");
            if (AddOnManifest.IsReserved(entry.FullName))
                continue;
            if (!manifest.Files.ContainsKey(entry.FullName))
                throw new AddOnPackageException($"The package has a file its manifest doesn't list: '{entry.FullName}'.");
        }
        foreach (var name in manifest.Files.Keys)
        {
            if (!seen.Contains(name))
                throw new AddOnPackageException($"The file '{name}' is missing from the package.");
        }
    }

    private static ZipArchiveEntry Entry(ZipArchive zip, string name) =>
        zip.GetEntry(name) ?? throw new AddOnPackageException($"The file '{name}' is missing from the package.");

    private static (string? Publisher, AddOnTrust Trust) CheckSignature(ZipArchive zip, byte[] manifestBytes, AddOnTrustStore trust)
    {
        var signatureEntry = zip.GetEntry(SignatureFileName);
        var keyEntry = zip.GetEntry(KeyFileName);
        if (signatureEntry is null && keyEntry is null)
            return (null, AddOnTrust.Unsigned);
        if (signatureEntry is null || keyEntry is null || signatureEntry.Length > 64 * 1024 || keyEntry.Length > 64 * 1024)
            throw new AddOnPackageException("The package's signature is incomplete.");
        return Verify(manifestBytes, ReadText(signatureEntry), ReadText(keyEntry), trust);
    }

    private static (string? Publisher, AddOnTrust Trust) Verify(byte[] manifest, string signature, string publicKeyPem, AddOnTrustStore trust)
    {
        bool valid;
        string fingerprint;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            fingerprint = AddOnTrustStore.Fingerprint(rsa);
            valid = rsa.VerifyData(manifest, Convert.FromBase64String(signature.Trim()), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            throw new AddOnPackageException("The package's signature can't be read.", ex);
        }
        if (!valid)
            throw new AddOnPackageException("The package's signature doesn't match: it was changed after it was signed.");
        return (fingerprint, trust.IsTrusted(fingerprint) ? AddOnTrust.TrustedPublisher : AddOnTrust.UnknownPublisher);
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.ASCII);
        return reader.ReadToEnd();
    }

    private static void Write(ZipArchive zip, string name, byte[] bytes)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        stream.Write(bytes);
    }

    /// <summary>True when the SHA-256 of what <paramref name="stream"/> holds is <paramref name="hex"/>.</summary>
    public static bool HashMatches(Stream stream, string hex) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), Convert.FromHexString(hex));

    public static bool HashMatches(byte[] bytes, string hex) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(hex));

    private static void TryDelete(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>
/// The publishers whose add-ons are trusted: Vox's own key (assets/config/addon-key.pem beside Vox,
/// which also signs the catalogue) and keys the user or administrator added (*.pem in
/// %APPDATA%\Vox\addons\trusted-keys). Keys are compared by fingerprint (SHA-256 of the public key).
/// </summary>
public sealed class AddOnTrustStore
{
    public const string VoxKeyFileName = "addon-key.pem";

    private readonly HashSet<string> _fingerprints;
    private readonly List<string> _keys = [];

    public AddOnTrustStore(IEnumerable<string> publicKeyPems)
    {
        _fingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pem in publicKeyPems)
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(pem);
                if (_fingerprints.Add(Fingerprint(rsa)))
                    _keys.Add(pem);
            }
            catch (Exception ex) when (ex is ArgumentException or CryptographicException)
            {
                // Not a key: ignored
            }
        }
    }

    public static readonly AddOnTrustStore Empty = new([]);

    /// <summary>The keys in <paramref name="voxKeyPath"/> (if it exists) and the .pem files in <paramref name="userKeysDirectory"/>.</summary>
    public static AddOnTrustStore Load(string? voxKeyPath, string? userKeysDirectory)
    {
        var pems = new List<string>();
        if (voxKeyPath is not null && File.Exists(voxKeyPath))
            pems.Add(File.ReadAllText(voxKeyPath));
        if (userKeysDirectory is not null && Directory.Exists(userKeysDirectory))
            pems.AddRange(Directory.EnumerateFiles(userKeysDirectory, "*.pem").Select(File.ReadAllText));
        return new AddOnTrustStore(pems);
    }

    public bool IsTrusted(string fingerprint) => _fingerprints.Contains(fingerprint);

    /// <summary>Whether any key is trusted at all (no key: no catalogue can be checked).</summary>
    public bool HasKeys => _keys.Count > 0;

    /// <summary>True when <paramref name="signature"/> (base64 RSA-SHA256) is a trusted key's signature of <paramref name="data"/>.</summary>
    public bool IsSignedByTrustedKey(byte[] data, string signature) =>
        _keys.Any(key => Updates.UpdateFeed.IsSigned(data, signature, key));

    /// <summary>The key's fingerprint: the SHA-256 of its SubjectPublicKeyInfo, as 64 hex digits.</summary>
    public static string Fingerprint(RSA key) => Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

    /// <summary>A fingerprint as people read it: the first 16 hex digits in groups of four.</summary>
    public static string Short(string fingerprint) =>
        string.Join(' ', Enumerable.Range(0, Math.Min(4, fingerprint.Length / 4)).Select(i => fingerprint.Substring(i * 4, 4)));
}
