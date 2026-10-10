using System.Text.Json.Nodes;

namespace Vox.Core.Configuration;

/// <summary>
/// Brings a settings file written by an older Vox up to <see cref="VoxSettings.CurrentSchemaVersion"/>,
/// one version at a time, on the JSON before it is read. A file without a SchemaVersion is version 1.
/// </summary>
public static class SettingsMigrations
{
    /// <summary>Each step takes a file of version <c>From</c> to <c>From + 1</c>.</summary>
    private static readonly (int From, Action<JsonObject> Apply)[] Steps =
    [
        // 2: sleep-mode apps are process names; "notepad.exe" as typed by users never matched
        (1, settings =>
        {
            if (Property(settings, nameof(VoxSettings.SleepApps)) is JsonArray apps)
            {
                for (int i = 0; i < apps.Count; i++)
                {
                    if (apps[i]?.GetValueKind() == System.Text.Json.JsonValueKind.String
                        && apps[i]!.GetValue<string>() is { } app
                        && app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        apps[i] = app[..^4];
                }
            }
        }),
    ];

    /// <summary>The schema version of a settings file (1 when it has none).</summary>
    public static int VersionOf(JsonObject settings) =>
        Property(settings, nameof(VoxSettings.SchemaVersion)) is JsonValue value && value.TryGetValue(out int version)
            ? version
            : 1;

    /// <summary>
    /// Migrates <paramref name="settings"/> in place to the current version. Returns the version
    /// it had. A file from a newer Vox is left as it is.
    /// </summary>
    public static int Migrate(JsonObject settings)
    {
        int original = VersionOf(settings);
        int version = original;
        foreach (var (from, apply) in Steps)
        {
            if (version == from)
            {
                apply(settings);
                version = from + 1;
            }
        }
        if (version != original)
        {
            Remove(settings, nameof(VoxSettings.SchemaVersion));
            settings[nameof(VoxSettings.SchemaVersion)] = version;
        }
        return original;
    }

    // Settings files are read case-insensitively
    private static JsonNode? Property(JsonObject settings, string name) =>
        settings.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static void Remove(JsonObject settings, string name)
    {
        foreach (var key in settings.Select(p => p.Key).Where(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)).ToList())
            settings.Remove(key);
    }
}
