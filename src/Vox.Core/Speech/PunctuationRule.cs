using System.Text;
using System.Text.RegularExpressions;
using Vox.Core.Configuration;

namespace Vox.Core.Speech;

/// <summary>
/// Speaks symbols (punctuation, emoji) by name according to the punctuation level: a symbol is
/// named when its level is at or below the setting; otherwise it is dropped, or kept (for the
/// synthesizer's pause) when the dictionary says so. A symbol on its own (reading a character)
/// is always named, and a dot or comma between digits (3.14, 1,000) is left to the synthesizer.
/// The invisible emoji variation selector (U+FE0F) is ignored.
/// </summary>
public sealed class PunctuationRule : ITextRule
{
    private const char VariationSelector = '️';

    private readonly SymbolDictionary _symbols;
    private readonly Func<PunctuationLevel> _level;
    // Symbols by their first character, longest first (so "..." wins over ".")
    private readonly Dictionary<char, string[]> _byFirstChar;
    private static readonly Regex Spaces = new(@"[ \t]{2,}", RegexOptions.Compiled);
    // Space left before a kept punctuation mark by a dropped symbol ("(cartoon)," → "cartoon ,")
    private static readonly Regex SpaceBeforePunctuation = new(@"[ \t]+(?=[.,;:?!…](\s|$))", RegexOptions.Compiled);

    public PunctuationRule(SymbolDictionary symbols, Func<PunctuationLevel> level)
    {
        _symbols = symbols;
        _level = level;
        _byFirstChar = symbols.Entries
            .Select(e => e.Symbol)
            .GroupBy(s => s[0])
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Length).ToArray());
    }

    public string Apply(string text, Utterance utterance)
    {
        // Invisible, and the dictionary's emoji are without it (also inside sequences)
        var original = text;
        if (text.IndexOf(VariationSelector) >= 0)
            text = text.Replace(VariationSelector.ToString(), string.Empty);

        var trimmed = text.Trim();
        if (trimmed.Length > 0 && _symbols.TryGet(trimmed, out var single))
            return single.Name;

        var level = _level();
        StringBuilder? result = null;
        int copiedTo = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (Match(text, i) is not { } symbol)
                continue;
            if (symbol is "." or "," && IsNumberSeparator(text, i))
                continue;

            _symbols.TryGet(symbol, out var entry);
            result ??= new StringBuilder(text.Length + 16);
            result.Append(text, copiedTo, i - copiedTo);
            if (entry.Level <= level)
                result.Append(' ').Append(entry.Name).Append(' ');
            else
                result.Append(entry.Preserve ? symbol : " ");
            i += symbol.Length - 1;
            copiedTo = i + 1;
        }

        if (result is null)
            return ReferenceEquals(text, original) ? original : text;
        result.Append(text, copiedTo, text.Length - copiedTo);
        return SpaceBeforePunctuation.Replace(Spaces.Replace(result.ToString(), " "), "").Trim();
    }

    /// <summary>The longest symbol starting at <paramref name="index"/>, or null.</summary>
    private string? Match(string text, int index)
    {
        if (!_byFirstChar.TryGetValue(text[index], out var candidates))
            return null;
        foreach (var candidate in candidates)
        {
            if (string.CompareOrdinal(text, index, candidate, 0, candidate.Length) == 0)
                return candidate;
        }
        return null;
    }

    /// <summary>A dot or comma with digits on both sides: part of a number.</summary>
    private static bool IsNumberSeparator(string text, int index) =>
        index > 0 && index + 1 < text.Length && char.IsDigit(text[index - 1]) && char.IsDigit(text[index + 1]);
}
