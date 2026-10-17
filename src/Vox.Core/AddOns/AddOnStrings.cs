using System.Globalization;
using System.Text.Json;
using Vox.Core.Localization;

namespace Vox.Core.AddOns;

/// <summary>
/// An add-on's string table: <c>strings/&lt;culture&gt;.json</c> files in its package (flat objects of
/// key to text, "fr.json", "pt-BR.json", "en.json"), each checked against the manifest's SHA-256.
/// A command name, description or settings label written as <c>@key</c> is looked up in
/// <see cref="VoxStrings.EffectiveCulture"/> (the full culture, then its language), then English, and
/// is the key itself when nothing has it; <c>@@text</c> is the literal "@text".
/// </summary>
public sealed class AddOnStrings
{
    public const string Folder = "strings";
    public const long MaxFileBytes = 256 * 1024;

    public static readonly AddOnStrings Empty = new(new Dictionary<string, IReadOnlyDictionary<string, string>>());

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _tables;

    public AddOnStrings(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> tables) =>
        _tables = new Dictionary<string, IReadOnlyDictionary<string, string>>(tables, StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => _tables.Count == 0;

    /// <summary>Reads the string files of the add-on in <paramref name="folder"/>; one that is damaged or changed is left out.</summary>
    public static AddOnStrings Load(string folder, AddOnManifest manifest, Action<string, Exception>? onProblem = null)
    {
        var tables = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var directory = Path.Combine(folder, Folder);
        if (!Directory.Exists(directory))
            return Empty;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var name = $"{Folder}/{Path.GetFileName(path)}";
            try
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length > MaxFileBytes || !manifest.Files.TryGetValue(name, out var hash) || !AddOnPackage.HashMatches(bytes, hash))
                    throw new FormatException("The string file is too large or isn't the one the manifest lists.");
                tables[Path.GetFileNameWithoutExtension(path)] = Parse(System.Text.Encoding.UTF8.GetString(bytes));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or JsonException)
            {
                onProblem?.Invoke(name, ex);
            }
        }
        return tables.Count == 0 ? Empty : new AddOnStrings(tables);
    }

    /// <summary>Reads a flat JSON object of text values (others are ignored).</summary>
    public static Dictionary<string, string> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new FormatException("A string file is a JSON object.");
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
                table[property.Name] = property.Value.GetString()!;
        }
        return table;
    }

    /// <summary>The text for <paramref name="key"/> in the current language, falling back to English; null when there is none.</summary>
    public string? Lookup(string key)
    {
        for (var culture = VoxStrings.EffectiveCulture; !culture.Equals(CultureInfo.InvariantCulture); culture = culture.Parent)
        {
            if (_tables.TryGetValue(culture.Name, out var table) && table.TryGetValue(key, out var text))
                return text;
        }
        return _tables.TryGetValue("en", out var english) && english.TryGetValue(key, out var fallback) ? fallback : null;
    }

    /// <summary><paramref name="text"/> with a leading <c>@key</c> looked up (see the class remarks); other text unchanged.</summary>
    public string Resolve(string text)
    {
        if (text.Length < 2 || text[0] != '@')
            return text;
        if (text[1] == '@')
            return text[1..];
        var key = text[1..];
        return Lookup(key) ?? key;
    }
}
