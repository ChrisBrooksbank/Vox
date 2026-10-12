using System.Text;
using Vox.Core.Buffer;
using Vox.Core.Localization;

namespace Vox.Core.Navigation;

/// <summary>
/// A page's summary worked out from its structure on this computer (no AI): its title, main
/// heading, reading time, sections and how its main content opens, one per line so it can be
/// reviewed line by line. Used when no describer is set up, or none may be used.
/// </summary>
public static class PageDigest
{
    private const int WordsPerMinute = 200;
    private const int MaxSections = 6;
    private const int MaxOpeningLength = 400;

    public static string Summarize(VBufferDocument document, string? mainContentKey = null)
    {
        var lines = new List<string>();
        var title = document.Root.Name.Trim();
        lines.Add(title.Length > 0 ? title : VoxStrings.Get("PageDigest.Untitled"));

        var main = MainContent.Best(document);
        var h1 = document.Headings.FirstOrDefault(h => h.HeadingLevel == 1);
        if (h1 is not null && TextOf(document, h1) is { Length: > 0 } heading && !string.Equals(heading, title, StringComparison.OrdinalIgnoreCase))
            lines.Add(VoxStrings.Format("PageDigest.MainHeading", heading));

        int words = CountWords(document.FlatText);
        int minutes = Math.Max(1, (int)Math.Round(words / (double)WordsPerMinute));
        lines.Add(minutes == 1 ? VoxStrings.Get("PageDigest.ReadingTimeOne") : VoxStrings.Format("PageDigest.ReadingTime", minutes));

        var sections = document.Headings
            .Where(h => h.HeadingLevel == 2)
            .Select(h => TextOf(document, h))
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (sections.Count > 0)
        {
            var shown = string.Join(", ", sections.Take(MaxSections));
            lines.Add(sections.Count > MaxSections
                ? VoxStrings.Format("PageDigest.SectionsMore", shown, sections.Count - MaxSections)
                : VoxStrings.Format("PageDigest.Sections", shown));
        }

        if (main is not null && Opening(document, main.Node) is { Length: > 0 } opening)
            lines.Add(VoxStrings.Format("PageDigest.Opening", opening));
        if (main is not null && mainContentKey is not null)
            lines.Add(VoxStrings.Format("PageDigest.MainContentKey", mainContentKey));
        return string.Join("\n", lines);
    }

    /// <summary>The page as text for a describer: its title, then its text.</summary>
    public static string TextOf(VBufferDocument document)
    {
        var title = document.Root.Name.Trim();
        return title.Length > 0 ? $"{title}\n\n{document.FlatText}" : document.FlatText;
    }

    /// <summary>A node's text (its whole subtree) on one line.</summary>
    public static string TextOf(VBufferDocument document, VBufferNode node)
    {
        var (start, end) = VBufferDocument.SubtreeSpan(node);
        if (end <= start || end > document.FlatText.Length)
            return node.Name.Trim();
        return Collapse(document.FlatText.AsSpan(start, end - start));
    }

    /// <summary>The first sentence or two from <paramref name="node"/> on, skipping its heading.</summary>
    private static string Opening(VBufferDocument document, VBufferNode node)
    {
        int start = VBufferDocument.SubtreeSpan(node).Start;
        // Past the heading the main content may start with: its text is said already
        var at = document.FindNodeAtOffset(start);
        for (var n = at; n is not null; n = n.Parent)
        {
            if (n.IsHeading)
            {
                start = VBufferDocument.SubtreeSpan(n).End;
                break;
            }
        }
        var text = document.FlatText;
        if (start >= text.Length)
            return string.Empty;
        var opening = Collapse(text.AsSpan(start, Math.Min(MaxOpeningLength, text.Length - start)));
        // End at a sentence where there is one
        int end = -1;
        for (int i = 0; i < opening.Length - 1; i++)
        {
            if (opening[i] is '.' or '!' or '?' && opening[i + 1] == ' ' && i >= 40)
            {
                end = i + 1;
                break;
            }
        }
        return end > 0 ? opening[..end] : opening;
    }

    private static string Collapse(ReadOnlySpan<char> text)
    {
        var result = new StringBuilder(text.Length);
        bool space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                space = result.Length > 0;
                continue;
            }
            if (space)
                result.Append(' ');
            space = false;
            result.Append(c);
        }
        return result.ToString();
    }

    private static int CountWords(string text)
    {
        int words = 0;
        bool inWord = false;
        foreach (var c in text)
        {
            bool letter = !char.IsWhiteSpace(c);
            if (letter && !inWord)
                words++;
            inWord = letter;
        }
        return words;
    }
}
