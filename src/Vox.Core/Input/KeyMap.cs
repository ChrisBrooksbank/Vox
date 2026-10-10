using System.Text.Json;
using System.Text.Json.Serialization;
using Vox.Core.Configuration;
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
/// A binding as written in a keymap file: <paramref name="Mode"/> is "Browse", "Focus" or "Any";
/// <paramref name="Command"/> a <see cref="NavigationCommand"/> name, or "None" (unbinds, in a user keymap).
/// </summary>
public sealed record KeyBinding(KeyModifiers Modifiers, int VkCode, string Mode, string Command, bool PassThrough = false);

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
    public static KeyMap LoadBuiltIn(out IReadOnlyList<string> warnings) => LoadBuiltIn(KeyboardLayout.Desktop, out warnings);

    /// <summary>The built-in keymap for <paramref name="layout"/>.</summary>
    public static KeyMap LoadBuiltIn(KeyboardLayout layout, out IReadOnlyList<string> warnings) =>
        Compose(ReadResource(DesktopFileName), layout == KeyboardLayout.Laptop ? ReadResource(LaptopFileName) : null, out warnings);

    /// <summary>File name of the desktop layout (the base of every layout).</summary>
    public const string DesktopFileName = "default-keymap.json";

    /// <summary>File name of the laptop layout's own bindings.</summary>
    public const string LaptopFileName = "laptop-keymap.json";

    /// <summary>
    /// Loads <paramref name="layout"/> from the keymap files in <paramref name="configDirectory"/>,
    /// falling back to the built-in keymap of that layout (and reporting why through
    /// <paramref name="error"/>) if a file is missing or invalid. The desktop layout is
    /// <see cref="DesktopFileName"/>; the laptop layout is that without its keypad bindings, plus
    /// <see cref="LaptopFileName"/>, whose bindings replace any on the same key.
    /// </summary>
    public static KeyMap LoadLayout(string configDirectory, KeyboardLayout layout, out Exception? error,
        out IReadOnlyList<string> warnings) =>
        LoadLayout(configDirectory, layout, userKeyMapPath: null, out error, out warnings);

    /// <summary>
    /// <see cref="LoadLayout(string, KeyboardLayout, out Exception?, out IReadOnlyList{string})"/>
    /// with the user's own bindings (<paramref name="userKeyMapPath"/>, see <see cref="UserFileName"/>)
    /// layered on top: each replaces the layout's binding on the same key and mode, and a binding to
    /// the command "None" unbinds the key. Conflicts are reported through <paramref name="warnings"/>.
    /// A missing user file is no error; an unreadable one is reported there and left out.
    /// </summary>
    public static KeyMap LoadLayout(string configDirectory, KeyboardLayout layout, string? userKeyMapPath,
        out Exception? error, out IReadOnlyList<string> warnings)
    {
        var userWarnings = new List<string>();
        List<KeyBindingEntry>? user = null;
        if (userKeyMapPath is not null && File.Exists(userKeyMapPath))
        {
            try
            {
                user = Parse(File.ReadAllText(userKeyMapPath)).Bindings;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                userWarnings.Add($"Your keymap {userKeyMapPath} could not be read ({ex.Message}); using the standard keys.");
            }
        }

        string desktop;
        string? laptop;
        try
        {
            desktop = File.ReadAllText(Path.Combine(configDirectory, DesktopFileName));
            laptop = layout == KeyboardLayout.Laptop ? File.ReadAllText(Path.Combine(configDirectory, LaptopFileName)) : null;
            error = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex;
            desktop = ReadResource(DesktopFileName);
            laptop = layout == KeyboardLayout.Laptop ? ReadResource(LaptopFileName) : null;
        }

        try
        {
            var map = Compose(desktop, laptop, user, out var composeWarnings);
            warnings = [.. composeWarnings, .. userWarnings];
            return map;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            error = ex;
            var map = LoadBuiltIn(layout, out var builtInWarnings);
            warnings = [.. builtInWarnings, .. userWarnings];
            return map;
        }
    }

    /// <summary>The user's own key bindings, layered over the layout: %APPDATA%\Vox\keymap.json.</summary>
    public const string UserFileName = "keymap.json";

    public static string DefaultUserKeyMapPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vox", UserFileName);

    /// <summary>The command name that unbinds a key in the user keymap.</summary>
    public const string UnboundCommand = "None";

    /// <summary>
    /// The bindings of <paramref name="layout"/> as its files list them (the laptop layout: the
    /// desktop's off the keypad, then the laptop's), from <paramref name="configDirectory"/> or
    /// else the built-in ones. Entries with unknown modifiers are left out.
    /// </summary>
    public static IReadOnlyList<KeyBinding> LayoutBindings(string configDirectory, KeyboardLayout layout)
    {
        string desktop, laptop;
        try
        {
            desktop = File.ReadAllText(Path.Combine(configDirectory, DesktopFileName));
            laptop = File.ReadAllText(Path.Combine(configDirectory, LaptopFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            desktop = ReadResource(DesktopFileName);
            laptop = ReadResource(LaptopFileName);
        }
        var entries = Parse(desktop).Bindings.AsEnumerable();
        if (layout == KeyboardLayout.Laptop)
            entries = entries.Where(e => !NumpadKeys.IsKeypadBindingCode(e.VkCode)).Concat(Parse(laptop).Bindings);
        return ToBindings(entries);
    }

    /// <summary>The bindings in a keymap file's JSON (a user keymap's included); unknown modifiers are left out.</summary>
    public static IReadOnlyList<KeyBinding> ParseBindings(string json) => ToBindings(Parse(json).Bindings);

    private static List<KeyBinding> ToBindings(IEnumerable<KeyBindingEntry> entries)
    {
        var bindings = new List<KeyBinding>();
        foreach (var entry in entries)
        {
            if (TryParseModifiers(entry.Modifiers, out var modifiers))
                bindings.Add(new KeyBinding(modifiers, entry.VkCode, entry.Mode, entry.Command, entry.PassThrough));
        }
        return bindings;
    }

    /// <summary>A keymap file holding <paramref name="bindings"/> (the format the loaders read).</summary>
    public static string ToJson(IEnumerable<KeyBinding> bindings)
    {
        var file = new KeyMapFile
        {
            Bindings = bindings.Select(b => new KeyBindingEntry
            {
                Modifiers = b.Modifiers == KeyModifiers.None ? "None" : b.Modifiers.ToString().Replace(", ", "|"),
                VkCode = b.VkCode,
                Mode = b.Mode,
                Command = b.Command,
                PassThrough = b.PassThrough,
            }).ToList(),
        };
        return JsonSerializer.Serialize(file, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        });
    }

    private static string ReadResource(string fileName)
    {
        using var stream = typeof(KeyMap).Assembly.GetManifestResourceStream("Vox.Core." + fileName)
            ?? throw new InvalidOperationException($"Built-in keymap resource {fileName} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The desktop bindings, or for the laptop layout those off the keypad plus the laptop's own;
    /// then the user's, which replace (or with "None" remove) any on the same key.
    /// </summary>
    private static KeyMap Compose(string desktopJson, string? laptopJson, out IReadOnlyList<string> warnings) =>
        Compose(desktopJson, laptopJson, null, out warnings);

    private static KeyMap Compose(string desktopJson, string? laptopJson, List<KeyBindingEntry>? user, out IReadOnlyList<string> warnings)
    {
        var entries = Parse(desktopJson).Bindings.AsEnumerable();
        if (laptopJson is not null)
        {
            entries = entries.Where(e => !NumpadKeys.IsKeypadBindingCode(e.VkCode))
                .Concat(Parse(laptopJson).Bindings);
        }
        if (user is null)
            return Build(entries, out warnings);

        var layout = entries.ToList();
        var layoutMap = Build(layout, out _);
        var map = Build(layout.Concat(user), out var buildWarnings);
        warnings = [.. buildWarnings, .. UserConflicts(layoutMap, user, map)];
        return map;
    }

    /// <summary>
    /// What the user keymap gets wrong or takes away: two bindings of its own on one key (the last
    /// wins), and commands of the layout left with no key at all because the user's bindings took
    /// or removed theirs.
    /// </summary>
    private static List<string> UserConflicts(KeyMap layout, List<KeyBindingEntry> user, KeyMap map)
    {
        var conflicts = new List<string>();
        var userKeys = new Dictionary<(KeyModifiers, int, string), string>();
        foreach (var entry in user)
        {
            if (!TryParseModifiers(entry.Modifiers, out var modifiers))
                continue;
            var key = (modifiers, entry.VkCode, entry.Mode.Trim().ToLowerInvariant());
            if (userKeys.TryGetValue(key, out var earlier) && !string.Equals(earlier, entry.Command, StringComparison.OrdinalIgnoreCase))
                conflicts.Add($"Your keymap binds {DescribeKey(modifiers, entry.VkCode)} ({entry.Mode}) to both {earlier} and {entry.Command}; {entry.Command} is used.");
            userKeys[key] = entry.Command;
        }

        var stillBound = map._bindings.Values.Select(b => b.Command).ToHashSet();
        foreach (var command in layout._bindings.Values.Select(b => b.Command).Distinct())
        {
            if (!stillBound.Contains(command))
                conflicts.Add($"Your keymap leaves {CommandCatalog.Describe(command).Name} ({command}) without a key.");
        }
        return conflicts;
    }

    private static string DescribeKey(KeyModifiers modifiers, int vkCode) =>
        modifiers == KeyModifiers.None ? $"key {vkCode}" : $"{modifiers.ToString().Replace(", ", "+")}+key {vkCode}";

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
    public static KeyMap LoadFromJson(string json, out IReadOnlyList<string> warnings) =>
        Build(Parse(json).Bindings, out warnings);

    private static KeyMapFile Parse(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        return JsonSerializer.Deserialize<KeyMapFile>(json, options)
            ?? throw new InvalidOperationException("Failed to deserialize keymap JSON.");
    }

    /// <summary>A keymap of <paramref name="entries"/>; a later entry replaces an earlier one on the same key.</summary>
    private static KeyMap Build(IEnumerable<KeyBindingEntry> entries, out IReadOnlyList<string> warnings)
    {
        var map = new KeyMap();
        var warningList = new List<string>();
        foreach (var entry in entries)
        {
            if (!TryParseModifiers(entry.Modifiers, out var modifiers))
            {
                warningList.Add($"Binding for vkCode {entry.VkCode}: unrecognized modifiers '{entry.Modifiers}' — skipped.");
                continue;
            }

            // A user keymap's "None" unbinds the key
            if (string.Equals(entry.Command, UnboundCommand, StringComparison.OrdinalIgnoreCase))
            {
                bool any = string.Equals(entry.Mode, "Any", StringComparison.OrdinalIgnoreCase);
                foreach (var unboundMode in new[] { InteractionMode.Browse, InteractionMode.Focus })
                {
                    if (any || string.Equals(entry.Mode, unboundMode.ToString(), StringComparison.OrdinalIgnoreCase))
                        map._bindings.Remove(new KeyMapKey(modifiers, entry.VkCode, unboundMode));
                }
                map._anyBindings.Remove((modifiers, entry.VkCode));
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
                // A mode binding replacing an "Any" one: the key no longer has a binding outside documents
                map._anyBindings.Remove((modifiers, entry.VkCode));
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

    /// <summary>Every binding (an "Any" binding appears once per mode).</summary>
    public IEnumerable<(KeyMapKey Key, NavigationCommand Command)> Bindings =>
        _bindings.Select(b => (b.Key, b.Value.Command));

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
