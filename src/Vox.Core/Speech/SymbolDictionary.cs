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

    /// <summary>The built-in English symbols (symbols-en.json).</summary>
    public static SymbolDictionary LoadBuiltIn(string fileName = "symbols-en.json")
    {
        using var stream = typeof(SymbolDictionary).Assembly.GetManifestResourceStream("Vox.Core.speech." + fileName)
            ?? throw new InvalidOperationException($"Built-in symbol dictionary {fileName} is missing.");
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }
}
