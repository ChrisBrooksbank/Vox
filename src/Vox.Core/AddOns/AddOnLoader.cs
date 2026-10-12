using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Vox.Sdk;

namespace Vox.Core.AddOns;

/// <summary>An add-on's code, loaded: its entry object and how to unload it.</summary>
public interface ILoadedAddOn
{
    IAddOn Instance { get; }

    /// <summary>Lets the add-on's code go (its load context is unloaded once nothing refers to it).</summary>
    void Unload();
}

/// <summary>Loads an installed add-on's code (tests use a fake that hands out objects).</summary>
public interface IAddOnLoader
{
    /// <summary>Loads the add-on in <paramref name="directory"/> described by <paramref name="manifest"/>; throws <see cref="AddOnLoadException"/>.</summary>
    ILoadedAddOn Load(string directory, AddOnManifest manifest);
}

/// <summary>An add-on's code couldn't be loaded (missing or changed files, no entry class, a constructor that threw).</summary>
public sealed class AddOnLoadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Loads each add-on into its own collectible <see cref="AssemblyLoadContext"/>, so disabling,
/// removing or updating it lets its code go without restarting Vox. Assemblies are read into
/// memory (after their SHA-256 is checked against the manifest) rather than loaded from the file,
/// so the files are never locked and can be replaced by an update. Vox.Sdk and the framework come
/// from Vox itself, never from the add-on's folder, so the add-on's types and Vox's agree.
/// </summary>
public sealed class AssemblyAddOnLoader : IAddOnLoader
{
    public ILoadedAddOn Load(string directory, AddOnManifest manifest)
    {
        var context = new AddOnLoadContext(manifest.Id, directory, manifest);
        try
        {
            var assembly = context.LoadFile(manifest.EntryAssembly);
            var entryTypes = EntryTypes(assembly);
            if (entryTypes.Count != 1)
                throw new AddOnLoadException(entryTypes.Count == 0
                    ? $"{manifest.EntryAssembly} has no public class implementing Vox.Sdk.IAddOn."
                    : $"{manifest.EntryAssembly} has more than one class implementing Vox.Sdk.IAddOn.");
            IAddOn instance;
            try { instance = (IAddOn)Activator.CreateInstance(entryTypes[0])!; }
            catch (TargetInvocationException ex) { throw new AddOnLoadException($"The add-on's {entryTypes[0].Name} failed to start.", ex.InnerException ?? ex); }
            catch (MissingMethodException ex) { throw new AddOnLoadException($"The add-on's {entryTypes[0].Name} has no public parameterless constructor.", ex); }
            return new Loaded(instance, context);
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    private static List<Type> EntryTypes(Assembly assembly)
    {
        Type?[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types; }
        return types.Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && typeof(IAddOn).IsAssignableFrom(t)).Cast<Type>().ToList();
    }

    private sealed class Loaded(IAddOn instance, AddOnLoadContext context) : ILoadedAddOn
    {
        private IAddOn? _instance = instance;
        private AddOnLoadContext? _context = context;

        public IAddOn Instance => _instance ?? throw new ObjectDisposedException("add-on");

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Unload()
        {
            _instance = null;
            Interlocked.Exchange(ref _context, null)?.Unload();
        }
    }
}

/// <summary>
/// One add-on's assemblies. An assembly the add-on doesn't ship (or that Vox shares with every
/// add-on: Vox.Sdk, Microsoft.Extensions.Logging.Abstractions) resolves from Vox's own context.
/// </summary>
internal sealed class AddOnLoadContext(string id, string directory, AddOnManifest manifest)
    : AssemblyLoadContext($"AddOn:{id}", isCollectible: true)
{
    /// <summary>Assemblies an add-on always shares with Vox, whatever copy it ships.</summary>
    private static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase)
    {
        "Vox.Sdk", "Microsoft.Extensions.Logging.Abstractions", "Microsoft.Extensions.DependencyInjection.Abstractions",
    };

    public Assembly LoadFile(string fileName)
    {
        if (!manifest.Files.TryGetValue(fileName, out var hash))
            throw new AddOnLoadException($"{fileName} isn't one of the add-on's files.");
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
            throw new AddOnLoadException($"{fileName} is missing from the add-on's folder.");
        var bytes = File.ReadAllBytes(path);
        if (!AddOnPackage.HashMatches(bytes, hash))
            throw new AddOnLoadException($"{fileName} has changed since the add-on was installed.");
        try
        {
            using var stream = new MemoryStream(bytes);
            return LoadFromStream(stream);
        }
        catch (BadImageFormatException ex)
        {
            throw new AddOnLoadException($"{fileName} isn't a .NET assembly.", ex);
        }
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (name is null || Shared.Contains(name))
            return null;
        var fileName = name + ".dll";
        // Only the add-on's own files, never one from elsewhere
        if (manifest.Files.ContainsKey(fileName) && File.Exists(Path.Combine(directory, fileName)))
        {
            // Vox's (or the framework's) own copy wins: types must be the same ones
            if (Default.Assemblies.Any(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase)))
                return null;
            return LoadFile(fileName);
        }
        return null;
    }

    // Native libraries: only from the add-on's own files
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var fileName = unmanagedDllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? unmanagedDllName : unmanagedDllName + ".dll";
        return manifest.Files.ContainsKey(fileName) ? LoadUnmanagedDllFromPath(Path.Combine(directory, fileName)) : IntPtr.Zero;
    }
}
