using System.Text.RegularExpressions;
using Vox.Core.Configuration;

namespace Vox.Core.Speech;

/// <summary>
/// Speaks symbols by name according to the punctuation level: a symbol is named when its level
/// is at or below the setting; otherwise it is dropped, or kept (for the synthesizer's pause)
/// when the dictionary says so. A symbol on its own (reading a character) is always named, and
/// a dot or comma between digits (3.14, 1,000) is left to the synthesizer.
/// </summary>
public sealed class PunctuationRule : ITextRule
{
    private readonly SymbolDictionary _symbols;
    private readonly Func<PunctuationLevel> _level;
    private readonly Regex _pattern;
    private static readonly Regex Spaces = new(@"[ \t]{2,}", RegexOptions.Compiled);
    // Space left before a kept punctuation mark by a dropped symbol ("(cartoon)," → "cartoon ,")
    private static readonly Regex SpaceBeforePunctuation = new(@"[ \t]+(?=[.,;:?!…](\s|$))", RegexOptions.Compiled);

    public PunctuationRule(SymbolDictionary symbols, Func<PunctuationLevel> level)
    {
        _symbols = symbols;
        _level = level;
        // Longest symbols first, so "..." wins over "."
        var alternatives = symbols.Entries.Select(e => e.Symbol).OrderByDescending(s => s.Length).Select(Regex.Escape);
        _pattern = new Regex(string.Join("|", alternatives), RegexOptions.CultureInvariant);
    }

    public string Apply(string text, Utterance utterance)
    {
        var trimmed = text.Trim();
        if (trimmed.Length > 0 && _symbols.TryGet(trimmed, out var single))
            return single.Name;
        if (_symbols.Entries.Count == 0)
            return text;

        var level = _level();
        bool changed = false;
        var result = _pattern.Replace(text, match =>
        {
            var entry = Entry(match.Value);
            if (IsNumberSeparator(text, match))
                return match.Value;
            changed = true;
            if (entry.Level <= level)
                return $" {entry.Name} ";
            return entry.Preserve ? match.Value : " ";
        });
        return changed ? SpaceBeforePunctuation.Replace(Spaces.Replace(result, " "), "").Trim() : text;
    }

    private SymbolEntry Entry(string symbol) => _symbols.TryGet(symbol, out var entry) ? entry : throw new KeyNotFoundException();

    /// <summary>A dot or comma with digits on both sides: part of a number.</summary>
    private static bool IsNumberSeparator(string text, Match match) =>
        match.Value is "." or "," && match.Index > 0 && match.Index + 1 < text.Length
        && char.IsDigit(text[match.Index - 1]) && char.IsDigit(text[match.Index + 1]);
}
