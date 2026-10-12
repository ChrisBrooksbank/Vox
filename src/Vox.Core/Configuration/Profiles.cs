using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Vox.Core.Configuration;

/// <summary>
/// A configuration profile: a named, sparse set of settings that replace the normal
/// configuration's while it is active. It is active while one of <see cref="Apps"/> (process
/// names) is in the foreground, during Say All when <see cref="SayAll"/>, or when chosen by hand.
/// </summary>
public sealed record ConfigurationProfile(string Name, JsonObject Settings, IReadOnlyList<string> Apps, bool SayAll = false)
{
    public bool IsForApp(string? processName) =>
        processName is not null && Apps.Contains(processName, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Layers profiles' settings over the normal configuration, and finds what a change altered.</summary>
public static class ProfileSettings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The settings as JSON properties (enums as names, as in settings.json).</summary>
    public static JsonObject ToJson(VoxSettings settings) =>
        JsonSerializer.SerializeToNode(settings, Json)!.AsObject();

    /// <summary>
    /// <paramref name="settings"/> with each of <paramref name="layers"/> laid over it in order (a
    /// later layer wins). Properties a layer sets that settings don't have, or that don't parse, are
    /// left out with the rest of that layer still applied.
    /// </summary>
    public static VoxSettings Merge(VoxSettings settings, IEnumerable<JsonObject> layers)
    {
        var merged = ToJson(settings);
        foreach (var layer in layers)
        {
            foreach (var (name, value) in layer)
            {
                if (name == nameof(VoxSettings.SchemaVersion) || !merged.ContainsKey(name))
                    continue;
                var previous = merged[name]?.DeepClone();
                merged[name] = value?.DeepClone();
                if (!Parses(merged))
                    merged[name] = previous;
            }
        }
        return merged.Deserialize<VoxSettings>(Json) ?? settings;
    }

    /// <summary>The properties whose values differ from <paramref name="before"/> to <paramref name="after"/>, with their new values.</summary>
    public static JsonObject Diff(VoxSettings before, VoxSettings after)
    {
        var from = ToJson(before);
        var to = ToJson(after);
        var diff = new JsonObject();
        foreach (var (name, value) in to)
        {
            if (!JsonNode.DeepEquals(from[name], value))
                diff[name] = value?.DeepClone();
        }
        return diff;
    }

    private static bool Parses(JsonObject settings)
    {
        try { return settings.Deserialize<VoxSettings>(Json) is not null; }
        catch (JsonException) { return false; }
    }
}

/// <summary>
/// The profiles, one JSON file each in the user's profiles folder:
/// <c>{ "name": ..., "apps": [...], "sayAll": false, "settings": { ...only what differs } }</c>.
/// Without a folder (secure mode) there are none, and none can be made.
/// </summary>
public sealed class ProfileStore
{
    private readonly string? _directory;
    private readonly ILogger _logger;

    public ProfileStore(string? directory, ILogger<ProfileStore>? logger = null)
    {
        _directory = directory;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>The default folder, in the user's Vox folder.</summary>
    public static string DefaultDirectory => Path.Combine(Lifecycle.VoxPaths.UserData, "profiles");

    /// <summary>Whether profiles can be kept here.</summary>
    public bool IsAvailable => _directory is not null;

    /// <summary>Every profile that can be read; a file that can't is logged and left out.</summary>
    public IReadOnlyList<ConfigurationProfile> LoadAll()
    {
        if (_directory is null || !Directory.Exists(_directory))
            return [];
        var profiles = new List<ConfigurationProfile>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject file)
                    throw new JsonException("Not a JSON object");
                var name = file["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(path);
                var apps = file["apps"] is JsonArray array
                    ? array.Select(a => a?.GetValue<string>()).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a!).ToList()
                    : [];
                bool sayAll = file["sayAll"]?.GetValue<bool>() ?? false;
                var settings = file["settings"] as JsonObject ?? new JsonObject();
                profiles.Add(new ConfigurationProfile(name, (JsonObject)settings.DeepClone(), apps, sayAll));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
            {
                _logger.LogWarning(ex, "Could not read the profile {Path}; left out", path);
            }
        }
        return profiles;
    }

    /// <summary>Saves a profile (atomically), replacing the one with its name. Returns whether it worked.</summary>
    public bool Save(ConfigurationProfile profile)
    {
        if (_directory is null)
            return false;
        try
        {
            Directory.CreateDirectory(_directory);
            var file = new JsonObject
            {
                ["name"] = profile.Name,
                ["apps"] = new JsonArray([.. profile.Apps.Select(a => (JsonNode?)JsonValue.Create(a))]),
                ["sayAll"] = profile.SayAll,
                ["settings"] = profile.Settings.DeepClone(),
            };
            var path = PathOf(profile.Name);
            var temp = path + ".tmp";
            File.WriteAllText(temp, file.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not save the profile {Name}", profile.Name);
            return false;
        }
    }

    /// <summary>Deletes a profile's file.</summary>
    public void Delete(string name)
    {
        if (_directory is null)
            return;
        try { File.Delete(PathOf(name)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not delete the profile {Name}", name);
        }
    }

    /// <summary>A file name for a profile name: characters a file name can't have become "_".</summary>
    public static string FileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var safe = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return (safe.Length == 0 ? "profile" : safe) + ".json";
    }

    private string PathOf(string name) => Path.Combine(_directory!, FileName(name));
}
