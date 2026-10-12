using System.Text.Json;
using Vox.Core.Speech;

namespace Vox.Core.Switching;

/// <summary>A pronunciation entry brought over and where it came from ("default.dic:4").</summary>
public sealed record ImportedEntry(string Source, PronunciationEntry Entry);

/// <summary>
/// Adds imported pronunciation entries to one of Vox's dictionary files (the user's
/// <c>dictionaries/user.json</c> or a voice's), shared by the NVDA and JAWS importers. Vox's own
/// entries are kept; an imported entry with the same pattern, type and case replaces Vox's (the
/// report has the replacement it had); new ones go at the end.
/// </summary>
public static class DictionaryImport
{
    /// <summary>
    /// The file's new JSON with <paramref name="entries"/> merged in, or null when nothing would
    /// change or the existing file can't be read (reported; a file Vox can't read is never replaced).
    /// </summary>
    /// <param name="existingJson">The file's contents now, or null when there is none.</param>
    /// <param name="target">The file as the report names it ("dictionaries/user.json").</param>
    public static string? Merge(string? existingJson, IReadOnlyList<ImportedEntry> entries, string target, ImportReport report)
    {
        if (entries.Count == 0)
            return null;

        var result = new List<PronunciationEntry>();
        if (existingJson is not null)
        {
            try
            {
                result.AddRange(PronunciationDictionary.ReadEntries(existingJson));
            }
            catch (JsonException ex)
            {
                foreach (var entry in entries)
                    report.AddSkipped(entry.Source, entry.Entry.Pattern, ImportSkipReason.Unreadable, $"{target}: {ex.Message}");
                return null;
            }
        }

        bool changed = false;
        foreach (var (source, entry) in entries)
        {
            int index = result.FindIndex(e => SameRule(e, entry));
            string? previous = index >= 0 ? result[index].Replacement : null;
            report.AddImported(source, $"{target}: {entry.Pattern}", entry.Replacement, previous);
            if (index >= 0)
            {
                if (result[index] == entry)
                    continue;
                result[index] = entry;
            }
            else
                result.Add(entry);
            changed = true;
        }
        return changed ? PronunciationDictionary.ToJson(result) : null;
    }

    private static bool SameRule(PronunciationEntry a, PronunciationEntry b) =>
        a.Type == b.Type && a.CaseSensitive == b.CaseSensitive
        && string.Equals(a.Pattern, b.Pattern, a.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
