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

    /// <summary>When true the key still reaches the application after the command runs.</summary>
    [JsonPropertyName("passThrough")]
    public bool PassThrough { get; set; }
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
/// Bindings with mode "Any" match both Browse and Focus modes, and are the only bindings that
/// apply outside web documents (<see cref="TryResolveOutsideDocument"/>).
/// Bound keys are swallowed by the keyboard hook unless the binding sets "passThrough".
/// Read-only after loading, so lookups are safe from any thread (including the hook thread).
/// </summary>
public sealed class KeyMap
{
    private readonly record struct Binding(NavigationCommand Command, bool PassThrough);

    private readonly Dictionary<KeyMapKey, Binding> _bindings = new();
    private readonly Dictionary<(KeyModifiers, int), Binding> _anyBindings = new();

    private KeyMap() { }

    /// <summary>
    /// Loads a KeyMap from the given JSON file path.
    /// </summary>
    public static KeyMap LoadFromFile(string filePath) => LoadFromFile(filePath, out _);

    /// <summary>
    /// Loads a KeyMap from the given JSON file path, also reporting any entries that were
    /// skipped because of an unrecognized modifier, command or mode name.
    /// </summary>
    public static KeyMap LoadFromFile(string filePath, out IReadOnlyList<string> warnings)
    {
        var json = File.ReadAllText(filePath);
        return LoadFromJson(json, out warnings);
    }

    /// <summary>
    /// Loads the default keymap embedded in Vox.Core (a copy of assets/config/default-keymap.json).
    /// </summary>
    public static KeyMap LoadBuiltIn() => LoadBuiltIn(out _);

    /// <summary>
    /// Loads the built-in keymap, also reporting any entries that were skipped because of an
    /// unrecognized modifier, command or mode name.
    /// </summary>
    public static KeyMap LoadBuiltIn(out IReadOnlyList<string> warnings)
    {
        using var stream = typeof(KeyMap).Assembly.GetManifestResourceStream("Vox.Core.default-keymap.json")
            ?? throw new InvalidOperationException("Built-in keymap resource is missing.");
        using var reader = new StreamReader(stream);
        return LoadFromJson(reader.ReadToEnd(), out warnings);
    }

    /// <summary>
    /// Loads a KeyMap from <paramref name="filePath"/>, falling back to the built-in keymap (and
    /// reporting why through <paramref name="error"/>) if the file is missing or invalid. Also
    /// reports any entries — in whichever keymap was actually loaded — that were skipped because
    /// of an unrecognized modifier, command or mode name.
    /// </summary>
    public static KeyMap LoadFromFileOrBuiltIn(string filePath, out Exception? error, out IReadOnlyList<string> warnings)
    {
        try
        {
            error = null;
            return LoadFromFile(filePath, out warnings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            error = ex;
            return LoadBuiltIn(out warnings);
        }
    }

    /// <summary>
    /// Loads a KeyMap from <paramref name="filePath"/>, falling back to the built-in keymap (and
    /// reporting why through <paramref name="error"/>) if the file is missing or invalid.
    /// </summary>
    public static KeyMap LoadFromFileOrBuiltIn(string filePath, out Exception? error)
        => LoadFromFileOrBuiltIn(filePath, out error, out _);

    /// <summary>
    /// Loads a KeyMap from a JSON string.
    /// </summary>
    public static KeyMap LoadFromJson(string json) => LoadFromJson(json, out _);

    /// <summary>
    /// Loads a KeyMap from a JSON string, also reporting any entries that were skipped because of
    /// an unrecognized modifier, command or mode name (a typo in the JSON otherwise fails silently:
    /// the key simply does nothing, with no indication why).
    /// </summary>
    public static KeyMap LoadFromJson(string json, out IReadOnlyList<string> warnings)
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
        var warningList = new List<string>();
        foreach (var entry in file.Bindings)
        {
            if (!TryParseModifiers(entry.Modifiers, out var modifiers))
            {
                warningList.Add($"Binding for vkCode {entry.VkCode}: unrecognized modifiers '{entry.Modifiers}' — skipped.");
                continue;
            }

            if (!TryParseName<NavigationCommand>(entry.Command, out var command))
            {
                warningList.Add($"Binding for vkCode {entry.VkCode}: unrecognized command '{entry.Command}' — skipped.");
                continue;
            }

            var binding = new Binding(command, entry.PassThrough);
            if (string.Equals(entry.Mode, "Any", StringComparison.OrdinalIgnoreCase))
            {
                map._bindings[new KeyMapKey(modifiers, entry.VkCode, InteractionMode.Browse)] = binding;
                map._bindings[new KeyMapKey(modifiers, entry.VkCode, InteractionMode.Focus)] = binding;
                map._anyBindings[(modifiers, entry.VkCode)] = binding;
            }
            else if (TryParseName<InteractionMode>(entry.Mode, out var mode))
            {
                map._bindings[new KeyMapKey(modifiers, entry.VkCode, mode)] = binding;
            }
            else
            {
                warningList.Add($"Binding for vkCode {entry.VkCode}: unrecognized mode '{entry.Mode}' — skipped.");
            }
        }

        warnings = warningList;
        return map;
    }

    /// <summary>
    /// Tries to resolve a (Modifiers, VkCode, Mode) triple to a NavigationCommand.
    /// </summary>
    public bool TryResolve(KeyModifiers modifiers, int vkCode, InteractionMode mode, out NavigationCommand command)
        => TryResolve(modifiers, vkCode, mode, out command, out _);

    /// <summary>
    /// Tries to resolve a (Modifiers, VkCode, Mode) triple to a NavigationCommand, also reporting
    /// whether the key should still be passed through to the application.
    /// </summary>
    public bool TryResolve(KeyModifiers modifiers, int vkCode, InteractionMode mode,
        out NavigationCommand command, out bool passThrough)
    {
        if (_bindings.TryGetValue(new KeyMapKey(modifiers, vkCode, mode), out var binding))
        {
            command = binding.Command;
            passThrough = binding.PassThrough;
            return true;
        }
        command = default;
        passThrough = false;
        return false;
    }

    /// <summary>
    /// Resolves a key outside web documents, where only "Any" bindings apply
    /// (Browse- and Focus-mode bindings belong to documents).
    /// </summary>
    public bool TryResolveOutsideDocument(KeyModifiers modifiers, int vkCode,
        out NavigationCommand command, out bool passThrough)
    {
        if (_anyBindings.TryGetValue((modifiers, vkCode), out var binding))
        {
            command = binding.Command;
            passThrough = binding.PassThrough;
            return true;
        }
        command = default;
        passThrough = false;
        return false;
    }

    /// <summary>
    /// Returns the total number of bindings in this keymap.
    /// </summary>
    public int Count => _bindings.Count;

    /// <summary>
    /// Parses an enum member by name. Enum.TryParse alone also accepts any number ("99"), which
    /// would turn a typo into a binding that silently never fires.
    /// </summary>
    private static bool TryParseName<TEnum>(string? value, out TEnum result) where TEnum : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out result)
        && Enum.IsDefined(result)
        && !char.IsDigit(value!.Trim()[0]) && value.Trim()[0] is not '-' and not '+';

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
            if (!TryParseName<KeyModifiers>(part, out var flag))
                return false;
            result |= flag;
        }

        return true;
    }
}
