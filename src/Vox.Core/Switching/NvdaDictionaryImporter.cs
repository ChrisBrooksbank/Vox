using System.Text;
using System.Text.RegularExpressions;
using Vox.Core.Speech;

namespace Vox.Core.Switching;

/// <summary>
/// Reads NVDA's speech dictionaries (<c>speechDicts\default.dic</c> and the voice dictionaries in
/// <c>speechDicts\voiceDicts.v1\&lt;synth&gt;\</c>): one entry per line, tab-separated pattern,
/// replacement, case sensitive (0/1) and type (0 anywhere, 1 regular expression, 2 whole word),
/// <c>#</c> in the text written <c>\#</c>, lines starting with <c>#</c> comments. NVDA's regular
/// expressions are Python's: converted to .NET's where they mean the same (named groups,
/// <c>\1</c> and <c>\g&lt;name&gt;</c> in replacements), else reported as skipped.
/// </summary>
public static class NvdaDictionaryImporter
{
    public const string FolderName = "speechDicts";
    public const string DefaultFileName = "default.dic";
    public const string VoiceFolderName = "voiceDicts.v1";

    /// <summary>The entries of a .dic file's text; malformed lines and regular expressions Vox can't take are reported.</summary>
    public static IReadOnlyList<ImportedEntry> Parse(string text, string fileName, ImportReport report)
    {
        var entries = new List<ImportedEntry>();
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r').TrimStart('﻿');
            if (line.Trim().Length == 0 || line.StartsWith('#'))
                continue;
            string source = $"{fileName}:{i + 1}";
            var fields = line.Split('\t');
            if (fields.Length != 4 || fields[0].Length == 0
                || fields[2].Trim() is not ("0" or "1") || fields[3].Trim() is not ("0" or "1" or "2"))
            {
                report.AddSkipped(source, line.Trim(), ImportSkipReason.Malformed);
                continue;
            }

            string pattern = fields[0].Replace(@"\#", "#");
            string replacement = fields[1].Replace(@"\#", "#");
            bool caseSensitive = fields[2].Trim() == "1";
            var type = fields[3].Trim() switch
            {
                "1" => PronunciationMatch.Regex,
                "2" => PronunciationMatch.Word,
                _ => PronunciationMatch.Anywhere,
            };
            if (type == PronunciationMatch.Regex)
            {
                if (ConvertPattern(pattern) is not { } converted)
                {
                    report.AddSkipped(source, pattern, ImportSkipReason.Unsupported);
                    continue;
                }
                pattern = converted;
                replacement = ConvertReplacement(replacement);
            }
            entries.Add(new ImportedEntry(source, new PronunciationEntry(pattern, replacement, type, caseSensitive)));
        }
        return entries;
    }

    /// <summary>
    /// A Python regular expression as .NET's, or null when .NET would read it differently or not at
    /// all: <c>(?P&lt;name&gt;</c> becomes <c>(?&lt;name&gt;</c>, <c>(?P=name)</c> <c>\k&lt;name&gt;</c>,
    /// <c>\Z</c> <c>\z</c>; Python-only flags (<c>(?a)</c>, <c>(?L)</c>, <c>(?u)</c>) make it unsupported.
    /// </summary>
    public static string? ConvertPattern(string pattern)
    {
        var result = new StringBuilder();
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                char next = pattern[++i];
                result.Append(next == 'Z' ? @"\z" : "\\" + next);
            }
            else if (pattern.AsSpan(i).StartsWith("(?P<"))
            {
                result.Append("(?<");
                i += 3;
            }
            else if (pattern.AsSpan(i).StartsWith("(?P="))
            {
                int close = pattern.IndexOf(')', i);
                if (close < 0)
                    return null;
                result.Append(@"\k<").Append(pattern, i + 4, close - i - 4).Append('>');
                i = close;
            }
            else if (pattern.AsSpan(i).StartsWith("(?") && i + 2 < pattern.Length && IsPythonOnlyFlags(pattern, i + 2))
                return null;
            else
                result.Append(c);
        }

        string converted = result.ToString();
        try
        {
            _ = new Regex(converted, RegexOptions.CultureInvariant);
            return converted;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // "(?aiL)" and the like: inline flags with one .NET doesn't have
    private static bool IsPythonOnlyFlags(string pattern, int start)
    {
        int end = start;
        while (end < pattern.Length && char.IsLetter(pattern[end]))
            end++;
        if (end == start || end >= pattern.Length || pattern[end] is not (')' or ':' or '-'))
            return false;
        return pattern.AsSpan(start, end - start).IndexOfAny("aLu") >= 0;
    }

    /// <summary>
    /// A Python replacement as .NET's: <c>\1</c> and <c>\g&lt;1&gt;</c> / <c>\g&lt;name&gt;</c> become
    /// <c>${1}</c> / <c>${name}</c>, <c>\\</c>, <c>\n</c> and <c>\t</c> their characters, and a literal
    /// <c>$</c> <c>$$</c>.
    /// </summary>
    public static string ConvertReplacement(string replacement)
    {
        var result = new StringBuilder();
        for (int i = 0; i < replacement.Length; i++)
        {
            char c = replacement[i];
            if (c == '$')
            {
                result.Append("$$");
                continue;
            }
            if (c != '\\' || i + 1 >= replacement.Length)
            {
                result.Append(c);
                continue;
            }
            char next = replacement[i + 1];
            if (char.IsAsciiDigit(next))
            {
                int end = i + 1;
                while (end < replacement.Length && end < i + 3 && char.IsAsciiDigit(replacement[end]))
                    end++;
                result.Append("${").Append(replacement, i + 1, end - i - 1).Append('}');
                i = end - 1;
            }
            else if (next == 'g' && i + 2 < replacement.Length && replacement[i + 2] == '<' && replacement.IndexOf('>', i + 3) is int close and > 0)
            {
                result.Append("${").Append(replacement, i + 3, close - i - 3).Append('}');
                i = close;
            }
            else
            {
                result.Append(next switch
                {
                    'n' => "\n",
                    't' => "\t",
                    '\\' => "\\",
                    _ => "\\" + next,
                });
                i++;
            }
        }
        return result.ToString();
    }

    /// <summary>
    /// The voice a voice dictionary's file name is for: NVDA names it "&lt;synth&gt;-&lt;voice id&gt;.dic",
    /// the id with characters a file name can't have as "_". Null when the name isn't like that.
    /// </summary>
    public static string? VoiceIdOf(string fileName, string synth)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        string prefix = synth + "-";
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length ? name[prefix.Length..] : null;
    }
}
