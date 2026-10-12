using System.Globalization;

namespace Vox.Core.Switching;

/// <summary>A line of a settings file that could not be read, by number (from 1).</summary>
public sealed record MalformedLine(int Number, string Text);

/// <summary>A section of a <see cref="ConfigObjFile"/>: its values and its subsections, names compared ignoring case.</summary>
public sealed class ConfigObjSection
{
    public ConfigObjSection(string name, ConfigObjSection? parent = null)
    {
        Name = name;
        Parent = parent;
    }

    public string Name { get; }

    public ConfigObjSection? Parent { get; }

    /// <summary>The section's values in file order (a key given twice keeps the last value).</summary>
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, ConfigObjSection> Sections { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The section at a path of names ("speech", "oneCore"), or null.</summary>
    public ConfigObjSection? Section(params string[] path)
    {
        var section = this;
        foreach (var name in path)
        {
            if (section is null || !section.Sections.TryGetValue(name, out section))
                return null;
        }
        return section;
    }

    public string? Value(string key) => Values.TryGetValue(key, out var value) ? value : null;
}

/// <summary>
/// The INI dialect NVDA writes (Python's configobj): <c>key = value</c> lines, <c>[section]</c>,
/// <c>[[subsection]]</c> and deeper by the number of brackets, <c>#</c> comments, values optionally
/// quoted. Lines that don't fit are kept in <see cref="Malformed"/> and otherwise ignored, so one bad
/// line never loses the rest of the file. Shared by the NVDA importers (nvda.ini, gestures.ini).
/// </summary>
public sealed class ConfigObjFile
{
    private ConfigObjFile(ConfigObjSection root, IReadOnlyList<MalformedLine> malformed)
    {
        Root = root;
        Malformed = malformed;
    }

    /// <summary>The values before the first section and the top-level sections.</summary>
    public ConfigObjSection Root { get; }

    public IReadOnlyList<MalformedLine> Malformed { get; }

    public static ConfigObjFile Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var root = new ConfigObjSection(string.Empty);
        var malformed = new List<MalformedLine>();
        var current = root;
        // Keys under a bad section header are dropped rather than put in the wrong section
        bool inBadSection = false;
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string raw = lines[i].TrimEnd('\r');
            string line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith('['))
            {
                if (TryParseHeader(line, out int depth, out string name) && depth <= Depth(current) + 1)
                {
                    var parent = current;
                    while (Depth(parent) >= depth)
                        parent = parent.Parent!;
                    if (!parent.Sections.TryGetValue(name, out var section))
                    {
                        section = new ConfigObjSection(name, parent);
                        parent.Sections[name] = section;
                    }
                    current = section;
                    inBadSection = false;
                }
                else
                {
                    malformed.Add(new MalformedLine(i + 1, raw));
                    inBadSection = true;
                }
                continue;
            }

            int equals = line.IndexOf('=');
            string key = equals > 0 ? Unquote(line[..equals].Trim()) : string.Empty;
            if (key.Length == 0)
            {
                malformed.Add(new MalformedLine(i + 1, raw));
                continue;
            }
            if (inBadSection)
                continue;
            current.Values[key] = ParseValue(line[(equals + 1)..].Trim());
        }
        return new ConfigObjFile(root, malformed);
    }

    private static int Depth(ConfigObjSection section)
    {
        int depth = 0;
        for (var s = section; s.Parent is not null; s = s.Parent)
            depth++;
        return depth;
    }

    private static bool TryParseHeader(string line, out int depth, out string name)
    {
        // Strip a trailing comment: "[speech] # comment"
        int close = line.LastIndexOf(']');
        name = string.Empty;
        depth = 0;
        while (depth < line.Length && line[depth] == '[')
            depth++;
        if (close < 0)
            return false;
        int closing = 0;
        while (close - closing >= 0 && line[close - closing] == ']')
            closing++;
        string rest = line[(close + 1)..].Trim();
        if (closing != depth || (rest.Length > 0 && !rest.StartsWith('#')))
            return false;
        name = Unquote(line[depth..(close - closing + 1)].Trim());
        return name.Length > 0 && !name.Contains('[') && !name.Contains(']');
    }

    private static string ParseValue(string value)
    {
        if (value.Length >= 1 && value[0] is '"' or '\'')
        {
            int end = value.IndexOf(value[0], 1);
            // A list with a quoted item ("kb:NVDA+,", kb:a) is left as written for its reader to split
            if (end > 0 && value[(end + 1)..].TrimStart().StartsWith(','))
                return value;
            return end > 0 ? value[1..end] : value[1..];
        }
        // An unquoted value ends at a comment
        int comment = value.IndexOf(" #", StringComparison.Ordinal);
        return comment >= 0 ? value[..comment].TrimEnd() : value;
    }

    private static string Unquote(string text) =>
        text.Length >= 2 && text[0] is '"' or '\'' && text[^1] == text[0] ? text[1..^1] : text;

    /// <summary>A configobj boolean (True/False, on/off, yes/no, 1/0), or null when it isn't one.</summary>
    public static bool? ParseBool(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "true" or "on" or "yes" or "1" => true,
        "false" or "off" or "no" or "0" => false,
        _ => null,
    };

    /// <summary>An integer value, or null when it isn't one.</summary>
    public static int? ParseInt(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : null;
}
