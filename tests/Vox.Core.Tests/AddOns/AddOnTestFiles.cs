using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vox.Core.AddOns;

namespace Vox.Core.Tests.AddOns;

/// <summary>A temporary folder for add-on packages, installed add-ons and keys; deleted on dispose.</summary>
internal sealed class AddOnTestFiles : IDisposable
{
    public AddOnTestFiles()
    {
        Root = Path.Combine(Path.GetTempPath(), "VoxAddOnTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string AddOnsDirectory => Path.Combine(Root, "addons");

    public string PathOf(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>A source folder with addon.json for <paramref name="id"/> and the given files.</summary>
    public string Source(string id = "com.example.test", string version = "1.0.0", string minApi = "1.0",
        IDictionary<string, string>? files = null, string? maxApi = null)
    {
        var folder = PathOf("src-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        files ??= new Dictionary<string, string> { ["Test.dll"] = "not really an assembly" };
        foreach (var (name, content) in files)
        {
            var path = Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        var manifest = new Dictionary<string, object?>
        {
            ["id"] = id, ["name"] = $"Add-on {id}", ["version"] = version, ["author"] = "Example",
            ["minApiVersion"] = minApi, ["maxApiVersion"] = maxApi, ["entryAssembly"] = files.Keys.First(f => f.EndsWith(".dll")),
        };
        File.WriteAllText(Path.Combine(folder, AddOnManifest.FileName), JsonSerializer.Serialize(manifest));
        return folder;
    }

    /// <summary>A package of a <see cref="Source"/> folder, signed with <paramref name="key"/> when given.</summary>
    public string Package(string? source = null, RSA? key = null, string? name = null)
    {
        source ??= Source();
        var path = PathOf((name ?? Guid.NewGuid().ToString("N")) + AddOnPackage.Extension);
        AddOnPackage.Create(source, path, key?.ExportRSAPrivateKeyPem());
        return path;
    }

    /// <summary>Rewrites one entry of a package (tampering).</summary>
    public static void Replace(string package, string entryName, string content)
    {
        using var zip = ZipFile.Open(package, ZipArchiveMode.Update);
        zip.GetEntry(entryName)?.Delete();
        using var stream = zip.CreateEntry(entryName).Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    /// <summary>Adds an entry to a package.</summary>
    public static void Add(string package, string entryName, string content)
    {
        using var zip = ZipFile.Open(package, ZipArchiveMode.Update);
        using var stream = zip.CreateEntry(entryName).Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    public static AddOnTrustStore Trusting(params RSA[] keys) => new(keys.Select(k => k.ExportSubjectPublicKeyInfoPem()));

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
