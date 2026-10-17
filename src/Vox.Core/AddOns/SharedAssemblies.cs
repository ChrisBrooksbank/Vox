using System.Reflection;

namespace Vox.Core.AddOns;

/// <summary>
/// The assemblies Vox itself provides, which an add-on must share rather than load its own copy of
/// (types from two copies don't match). Taken from the runtime's trusted platform assemblies list
/// and the files beside the application, so the answer doesn't depend on what happens to be loaded yet.
/// </summary>
public sealed class SharedAssemblies
{
    private readonly Dictionary<string, string> _paths;

    /// <summary>Assembly simple name to the file Vox provides it from.</summary>
    public SharedAssemblies(IEnumerable<string> trustedPlatformAssemblies, string? applicationFolder)
    {
        _paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in trustedPlatformAssemblies)
            Add(path);
        if (!string.IsNullOrEmpty(applicationFolder) && Directory.Exists(applicationFolder))
        {
            foreach (var path in Directory.EnumerateFiles(applicationFolder, "*.dll"))
                Add(path);
        }
    }

    private void Add(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            _paths.TryAdd(Path.GetFileNameWithoutExtension(path), path);
    }

    /// <summary>Vox's own process: the trusted platform assemblies and Vox.App's folder.</summary>
    public static SharedAssemblies Current { get; } = new(
        ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries),
        AppContext.BaseDirectory);

    /// <summary>True when Vox provides an assembly of this name.</summary>
    public bool Contains(string name) => _paths.ContainsKey(name);

    /// <summary>The version of Vox's copy, or null when there is none or it can't be read.</summary>
    public Version? VersionOf(string name)
    {
        if (!_paths.TryGetValue(name, out var path))
            return null;
        try { return AssemblyName.GetAssemblyName(path).Version; }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException) { return null; }
    }
}
