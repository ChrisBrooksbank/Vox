using System.Text.Json;
using Vox.Core.Input;

namespace Vox.Core.AppModules;

/// <summary>
/// App modules' keys: <c>assets/apps/&lt;module id&gt;.json</c>, in the keymap file format,
/// embedded in Vox.Core. A module without a file adds no keys.
/// </summary>
public static class AppModuleKeyMaps
{
    /// <summary>The bindings of the module <paramref name="moduleId"/>, or none.</summary>
    public static IReadOnlyList<KeyBinding> Load(string moduleId)
    {
        using var stream = typeof(AppModuleKeyMaps).Assembly.GetManifestResourceStream($"Vox.Core.apps.{moduleId}.json");
        if (stream is null)
            return [];
        using var reader = new StreamReader(stream);
        try
        {
            return KeyMap.ParseBindings(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>Ids of the modules that have a keymap file.</summary>
    public static IReadOnlyList<string> ModuleIds() =>
        typeof(AppModuleKeyMaps).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("Vox.Core.apps.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n => n["Vox.Core.apps.".Length..^".json".Length])
            .ToList();
}
