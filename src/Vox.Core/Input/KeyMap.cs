using System.Text.Json;
using System.Text.Json.Serialization;
using Vox.Core.Pipeline;

namespace Vox.Core.Input;

/// <summary>
/// Represents a single keymap binding entry in the JSON file.
/// </summary>
internal sealed class KeyBindingEntry
{
    [JsonPropertyName("modifiers")]
    public string Modifiers { get; set; } = "None";

    [JsonPropertyName("vkCode")]
    public int VkCode { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "Any";

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;
}

internal sealed class KeyMapFile
{
    [JsonPropertyName("bindings")]
    public List<KeyBindingEntry> Bindings { get; set; } = [];
}

/// <summary>
/// Lookup key for a keymap entry: modifier combination, virtual key code, and interaction mode.
/// </summary>
public readonly record struct KeyMapKey(KeyModifiers Modifiers, int VkCode, InteractionMode Mode);

/// <summary>
/// Loads and resolves keyboard bindings from a JSON keymap file.
/// Maps (Modifiers, VkCode, InteractionMode) to NavigationCommand.
/// Bindings with mode "Any" match both Browse and Focus modes.
/// </summary>
public sealed class KeyMap
{
    private readonly Dictionary<KeyMapKey, NavigationCommand> _bindings = new();

    private KeyMap() { }

    /// <summary>
    /// Loads a KeyMap from the given JSON file path.
    /// </summary>
    public static KeyMap LoadFromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return LoadFromJson(json);
    }

    /// <summary>
    /// Loads a KeyMap from a JSON string.
    /// </summary>
    public static KeyMap LoadFromJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        var file = JsonSerializer.Deserialize<KeyMapFile>(json, options)
            ?? throw new InvalidOperationException("Failed to deserialize keymap JSON.");

        var map = new KeyMap();
        foreach (var entry in file.Bindings)
        {
            if (!TryParseModifiers(entry.Modifiers, out var modifiers))
                continue;

            if (!Enum.TryParse<NavigationCommand>(entry.Command, ignoreCase: true, out var command))
                continue;

            if (string.Equals(entry.Mode, "Any", StringComparison.OrdinalIgnoreCase))
            {
                map._bindings[new KeyMapKey(modifiers, entry.VkCode, InteractionMode.Browse)] = command;
                map._bindings[new KeyMapKey(modifiers, entry.VkCode, InteractionMode.Focus)] = command;
            }
            else if (Enum.TryParse<InteractionMode>(entry.Mode, ignoreCase: true, out var mode))
            {
                map._bindings[new KeyMapKey(modifiers, entry.VkCode, mode)] = command;
            }
        }

        return map;
    }

    /// <summary>
    /// Tries to resolve a (Modifiers, VkCode, Mode) triple to a NavigationCommand.
    /// </summary>
    public bool TryResolve(KeyModifiers modifiers, int vkCode, InteractionMode mode, out NavigationCommand command)
        => _bindings.TryGetValue(new KeyMapKey(modifiers, vkCode, mode), out command);

    /// <summary>
    /// Returns the total number of bindings in this keymap.
    /// </summary>
    public int Count => _bindings.Count;

    private static bool TryParseModifiers(string value, out KeyModifiers result)
    {
        result = KeyModifiers.None;

        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "None", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var part in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<KeyModifiers>(part, ignoreCase: true, out var flag))
                return false;
            result |= flag;
        }

        return true;
    }
}
