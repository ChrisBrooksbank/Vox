using System.Text.Json;
using System.Text.Json.Serialization;
using Vox.Core.Configuration;

namespace Vox.Core.Speech;

/// <summary>A symbol's spoken name, the punctuation level it is spoken from, and whether it is kept for its pause when not spoken.</summary>
public sealed record SymbolEntry(string Symbol, string Name, PunctuationLevel Level, bool Preserve);

/// <summary>
/// Symbols and their spoken names (assets/speech/symbols-en.json, embedded in Vox.Core), keyed
/// by the symbol (one or more characters, e.g. an emoji).
/// </summary>
public sealed class SymbolDictionary
{
    private sealed class File
    {
        [JsonPropertyName("symbols")]
        public List<Entry> Symbols { get; set; } = [];
    }

    private sealed class Entry
    {
        [JsonPropertyName("symbol")] public string Symbol { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("level")] public PunctuationLevel Level { get; set; } = PunctuationLevel.All;
        [JsonPropertyName("preserve")] public bool Preserve { get; set; }
    }

    private readonly Dictionary<string, SymbolEntry> _entries;

    public SymbolDictionary(IEnumerable<SymbolEntry> entries)
    {
        _entries = new Dictionary<string, SymbolEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.Symbol.Length > 0 && !string.IsNullOrWhiteSpace(entry.Name))
                _entries[entry.Symbol] = entry;
        }
    }

    public IReadOnlyCollection<SymbolEntry> Entries => _entries.Values;

    public bool TryGet(string symbol, out SymbolEntry entry) => _entries.TryGetValue(symbol, out entry!);

    /// <summary>Reads a dictionary file's JSON.</summary>
    public static SymbolDictionary FromJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var file = JsonSerializer.Deserialize<File>(json, options) ?? new File();
        return new SymbolDictionary(file.Symbols.Select(e => new SymbolEntry(e.Symbol, e.Name.Trim(), e.Level, e.Preserve)));
    }

    /// <summary>The built-in English symbols: emoji-en.json, with symbols-en.json taking precedence.</summary>
    public static SymbolDictionary LoadBuiltIn() =>
        new(ReadBuiltIn("emoji-en.json").Entries.Concat(ReadBuiltIn("symbols-en.json").Entries));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SymbolDictionary> ByLanguage = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The built-in symbols with their names in <paramref name="language"/> (a two-letter code, "fr"):
    /// the English dictionary with the names of <c>symbols-&lt;language&gt;.json</c> in place of
    /// the English ones, keeping the English levels; symbols only that language has are added as it
    /// gives them. English, or a language without a file, is <see cref="LoadBuiltIn"/>. Cached.
    /// </summary>
    public static SymbolDictionary ForLanguage(string? language)
    {
        language = string.IsNullOrWhiteSpace(language) ? "en" : language.Split('-')[0].ToLowerInvariant();
        return ByLanguage.GetOrAdd(language, static lang =>
        {
            var english = LoadBuiltIn();
            if (lang == "en" || !HasBuiltIn($"symbols-{lang}.json"))
                return english;
            var local = ReadBuiltIn($"symbols-{lang}.json");
            var entries = english.Entries.ToDictionary(e => e.Symbol, StringComparer.Ordinal);
            foreach (var entry in local.Entries)
            {
                entries[entry.Symbol] = entries.TryGetValue(entry.Symbol, out var englishEntry)
                    ? englishEntry with { Name = entry.Name }
                    : entry;
            }
            return new SymbolDictionary(entries.Values);
        });
    }

    /// <summary>The languages with symbol names of their own, besides English.</summary>
    public static IReadOnlyList<string> Languages =>
        typeof(SymbolDictionary).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("Vox.Core.speech.symbols-", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n => n["Vox.Core.speech.symbols-".Length..^".json".Length])
            .Where(l => l != "en")
            .ToList();

    private static bool HasBuiltIn(string fileName) =>
        typeof(SymbolDictionary).Assembly.GetManifestResourceInfo("Vox.Core.speech." + fileName) is not null;

    /// <summary>One built-in dictionary file.</summary>
    public static SymbolDictionary ReadBuiltIn(string fileName)
    {
        using var stream = typeof(SymbolDictionary).Assembly.GetManifestResourceStream("Vox.Core.speech." + fileName)
            ?? throw new InvalidOperationException($"Built-in symbol dictionary {fileName} is missing.");
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }
}
