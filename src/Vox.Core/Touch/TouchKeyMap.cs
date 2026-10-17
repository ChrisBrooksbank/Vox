using System.Text.Json;
using System.Text.Json.Serialization;
using Vox.Core.Input;
using Vox.Core.Pipeline;

namespace Vox.Core.Touch;

internal sealed class TouchBindingEntry
{
    [JsonPropertyName("gesture")]
    public string Gesture { get; set; } = string.Empty;

    [JsonPropertyName("fingers")]
    public int Fingers { get; set; } = 1;

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "Any";

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;
}

internal sealed class TouchKeyMapFile
{
    [JsonPropertyName("bindings")]
    public List<TouchBindingEntry> Bindings { get; set; } = [];
}

/// <summary>
/// Gesture bindings in the keymap format with a gesture and finger count in place of a key
/// (<c>assets/config/touch-keymap.json</c>; the user's <see cref="UserFileName"/> goes on top, and
/// the command "None" unbinds). A "Browse" or "Focus" binding applies only while a web document
/// has focus in that mode; "Any" bindings apply everywhere. Read-only after loading.
/// </summary>
public sealed class TouchKeyMap
{
    public const string UserFileName = "touch-keymap.json";

    private readonly Dictionary<(TouchGestureKind, int, string), NavigationCommand> _bindings = new();

    private TouchKeyMap() { }

    /// <summary>The built-in map with the user's file (if any) layered on top; problems are reported in <paramref name="warnings"/>.</summary>
    public static TouchKeyMap Load(string? userPath, out IReadOnlyList<string> warnings)
    {
        var list = new List<string>();
        var map = new TouchKeyMap();
        map.Apply(ReadBuiltIn(), list);
        if (userPath is not null && File.Exists(userPath))
        {
            try { map.Apply(Parse(File.ReadAllText(userPath)), list); }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                list.Add($"Could not read {userPath}: {ex.Message}");
            }
        }
        warnings = list;
        return map;
    }

    public static TouchKeyMap LoadBuiltIn() => Load(null, out _);

    /// <summary>The command for a gesture, or null; <paramref name="inDocument"/> says whether a web document has focus, <paramref name="mode"/> its mode.</summary>
    public NavigationCommand? Resolve(TouchGestureKind kind, int fingers, bool inDocument, InteractionMode mode)
    {
        if (inDocument && _bindings.TryGetValue((kind, fingers, mode.ToString()), out var specific))
            return specific;
        return _bindings.TryGetValue((kind, fingers, "Any"), out var any) ? any : null;
    }

    /// <summary>Every binding as (gesture, fingers, mode, command), for the keyboard reference.</summary>
    public IEnumerable<(TouchGestureKind Gesture, int Fingers, string Mode, NavigationCommand Command)> Bindings =>
        _bindings.Select(b => (b.Key.Item1, b.Key.Item2, b.Key.Item3, b.Value));

    private static List<TouchBindingEntry> Parse(string json) =>
        JsonSerializer.Deserialize<TouchKeyMapFile>(json)?.Bindings ?? [];

    private static List<TouchBindingEntry> ReadBuiltIn()
    {
        using var stream = typeof(TouchKeyMap).Assembly.GetManifestResourceStream("Vox.Core.touch-keymap.json")
            ?? throw new InvalidOperationException("Built-in touch keymap resource is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private void Apply(List<TouchBindingEntry> entries, List<string> warnings)
    {
        foreach (var e in entries)
        {
            if (!Enum.TryParse<TouchGestureKind>(e.Gesture, out var gesture) || gesture == TouchGestureKind.Explore)
            {
                warnings.Add($"Unknown gesture '{e.Gesture}'");
                continue;
            }
            if (e.Mode is not ("Any" or "Browse" or "Focus") || e.Fingers is < 1 or > 4)
            {
                warnings.Add($"Bad mode or finger count for gesture '{e.Gesture}'");
                continue;
            }
            var key = (gesture, e.Fingers, e.Mode);
            if (e.Command == "None")
            {
                _bindings.Remove(key);
                continue;
            }
            if (!Enum.TryParse<NavigationCommand>(e.Command, out var command))
            {
                warnings.Add($"Unknown command '{e.Command}'");
                continue;
            }
            _bindings[key] = command;
        }
    }
}
