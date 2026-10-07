using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Vox.Core.Speech;

/// <summary>How a pronunciation entry's pattern matches.</summary>
public enum PronunciationMatch
{
    /// <summary>The text anywhere, even inside a word.</summary>
    Anywhere,
    /// <summary>The text as a whole word.</summary>
    Word,
    /// <summary>A regular expression; the replacement may use $1 etc.</summary>
    Regex,
}

/// <summary>A pronunciation rule: say <see cref="Replacement"/> for text matching <see cref="Pattern"/>.</summary>
public sealed record PronunciationEntry(string Pattern, string Replacement, PronunciationMatch Type = PronunciationMatch.Word,
    bool CaseSensitive = false);

/// <summary>
/// A list of pronunciation rules, applied in order. Every rule's pattern is compiled once, with
/// a match timeout so a slow user regex can't stall speech (it is skipped for that utterance).
/// </summary>
public sealed class PronunciationDictionary
{
    public static readonly PronunciationDictionary Empty = new([]);

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);

    private readonly List<(Regex Pattern, PronunciationEntry Entry)> _rules = new();

    public PronunciationDictionary(IEnumerable<PronunciationEntry> entries) : this(entries, out _)
    {
    }

    /// <param name="warnings">Entries that were skipped (an empty or invalid pattern), by position.</param>
    public PronunciationDictionary(IEnumerable<PronunciationEntry> entries, out IReadOnlyList<string> warnings)
    {
        var problems = new List<string>();
        int index = 0;
        foreach (var entry in entries)
        {
            index++;
            if (string.IsNullOrEmpty(entry.Pattern))
            {
                problems.Add($"Entry {index}: empty pattern — skipped.");
                continue;
            }
            try
            {
                _rules.Add((Compile(entry), entry));
            }
            catch (ArgumentException)
            {
                problems.Add($"Entry {index}: invalid regular expression — skipped.");
            }
        }
        warnings = problems;
    }

    public int Count => _rules.Count;

    private static Regex Compile(PronunciationEntry entry)
    {
        var options = RegexOptions.CultureInvariant | (entry.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        var pattern = entry.Type switch
        {
            PronunciationMatch.Regex => entry.Pattern,
            // Not \b: it fails next to a pattern's own symbols ("C#")
            PronunciationMatch.Word => $@"(?<!\w){Regex.Escape(entry.Pattern)}(?!\w)",
            _ => Regex.Escape(entry.Pattern),
        };
        return new Regex(pattern, options, MatchTimeout);
    }

    /// <summary>The text with every rule applied in order.</summary>
    public string Apply(string text)
    {
        foreach (var (pattern, entry) in _rules)
        {
            try
            {
                text = entry.Type == PronunciationMatch.Regex
                    ? pattern.Replace(text, entry.Replacement)
                    : pattern.Replace(text, _ => entry.Replacement); // literal: "$" means "$"
            }
            catch (RegexMatchTimeoutException)
            {
                // Too slow on this text: leave it as it was for this rule
            }
        }
        return text;
    }

    private sealed class File
    {
        [JsonPropertyName("entries")]
        public List<PronunciationEntry> Entries { get; set; } = [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Reads a dictionary file's JSON ({"entries": [{"pattern", "replacement", "type", "caseSensitive"}]}).</summary>
    public static PronunciationDictionary FromJson(string json, out IReadOnlyList<string> warnings)
    {
        var file = JsonSerializer.Deserialize<File>(json, JsonOptions) ?? new File();
        return new PronunciationDictionary(file.Entries.Where(e => e is not null), out warnings);
    }

    /// <summary>The JSON for a dictionary file with <paramref name="entries"/>.</summary>
    public static string ToJson(IEnumerable<PronunciationEntry> entries) =>
        JsonSerializer.Serialize(new File { Entries = entries.ToList() }, JsonOptions);
}
