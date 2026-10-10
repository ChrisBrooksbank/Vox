namespace Vox.Core.Input;

/// <summary>A command found by <see cref="CommandSearch"/>, with its keys as people read them.</summary>
public sealed record CommandMatch(CommandInfo Command, IReadOnlyList<string> Keys)
{
    public override string ToString() =>
        $"{Command.Name}: {(Keys.Count == 0 ? "no key" : string.Join(", ", Keys))}";
}

/// <summary>
/// Finds commands by part of their name or description ("head" finds the heading commands), so
/// a user can learn a key without knowing which category it is in.
/// </summary>
public static class CommandSearch
{
    /// <summary>
    /// Commands whose name or description contains every word of <paramref name="query"/>:
    /// those whose name starts with it first, then by name match, then description match.
    /// </summary>
    public static IReadOnlyList<CommandMatch> Find(string query, GestureEditor keys, string screenReaderKey = "Insert")
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
            return [];
        return CommandCatalog.All
            .Select(info => (Info: info, Rank: Rank(info, query.Trim(), words)))
            .Where(m => m.Rank >= 0)
            .OrderBy(m => m.Rank)
            .ThenBy(m => m.Info.Name, StringComparer.OrdinalIgnoreCase)
            .Select(m => new CommandMatch(m.Info, keys.GesturesFor(m.Info.Command).Select(g => g.Describe(screenReaderKey)).ToList()))
            .ToList();
    }

    /// <summary>0: the name starts with the query; 1: every word is in the name; 2: in name or description; -1: no match.</summary>
    private static int Rank(CommandInfo info, string query, string[] words)
    {
        if (info.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (words.All(w => info.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
            return 1;
        var text = info.Name + " " + info.Description;
        return words.All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)) ? 2 : -1;
    }
}
