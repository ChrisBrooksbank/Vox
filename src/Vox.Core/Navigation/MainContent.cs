using Vox.Core.Buffer;
using Vox.Core.Localization;

namespace Vox.Core.Navigation;

/// <summary>A place the page's main content may start, and why it is suggested.</summary>
public sealed record MainContentSuggestion(VBufferNode Node, string Reason);

/// <summary>
/// Where a page's main content starts, past its navigation and banners, best guess first: the
/// main landmark, the first level 1 heading, and the longest passage of prose (for pages without
/// either). Worked out from the buffer alone, on this computer.
/// </summary>
public static class MainContent
{
    // Landmarks around the content, never in it
    private static readonly HashSet<string> SurroundingLandmarks = new(StringComparer.OrdinalIgnoreCase)
    {
        "Navigation", "Banner", "Content info", "Complementary", "Search",
    };

    // Shorter lines are menu items, buttons and captions rather than prose
    private const int MinProseLineLength = 60;

    public static IReadOnlyList<MainContentSuggestion> Suggestions(VBufferDocument document)
    {
        var suggestions = new List<MainContentSuggestion>();
        void Add(VBufferNode? node, string reason)
        {
            if (node is null || !node.HasText && node.Children.Count == 0 || !document.InScope(node))
                return;
            int start = VBufferDocument.SubtreeSpan(node).Start;
            if (suggestions.Any(s => VBufferDocument.SubtreeSpan(s.Node).Start == start))
                return;
            suggestions.Add(new MainContentSuggestion(node, reason));
        }

        Add(document.Landmarks.FirstOrDefault(l => string.Equals(l.LandmarkType, "Main", StringComparison.OrdinalIgnoreCase)),
            VoxStrings.Get("MainContent.Landmark"));
        Add(document.Headings.FirstOrDefault(h => h.HeadingLevel == 1 && !InSurroundings(h)),
            VoxStrings.Get("MainContent.Heading"));
        Add(LongestPassage(document), VoxStrings.Get("MainContent.Passage"));
        return suggestions;
    }

    /// <summary>The first suggestion, or null when the page has nothing to suggest.</summary>
    public static MainContentSuggestion? Best(VBufferDocument document) => Suggestions(document).FirstOrDefault();

    /// <summary>The node starting the longest run of prose lines outside navigation and banners.</summary>
    private static VBufferNode? LongestPassage(VBufferDocument document)
    {
        var text = document.FlatText;
        int bestStart = -1, bestLength = 0;
        int runStart = -1, runLength = 0;
        int lineStart = 0;
        while (lineStart <= text.Length)
        {
            int lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0)
                lineEnd = text.Length;
            int length = lineEnd - lineStart;
            bool prose = length >= MinProseLineLength && IsProse(document, lineStart);
            if (prose)
            {
                if (runStart < 0)
                {
                    runStart = lineStart;
                    runLength = 0;
                }
                runLength += length;
                if (runLength > bestLength)
                {
                    bestLength = runLength;
                    bestStart = runStart;
                }
            }
            else if (length > 0)
            {
                // A blank line between paragraphs doesn't end the passage; anything else does
                runStart = -1;
            }
            lineStart = lineEnd + 1;
        }
        return bestStart < 0 ? null : document.FindNodeAtOffset(bestStart);
    }

    private static bool IsProse(VBufferDocument document, int offset)
    {
        var node = document.FindNodeAtOffset(offset);
        if (node is null || InSurroundings(node))
            return false;
        // A line that starts with a link is a list of links more often than prose
        for (var n = node; n is not null; n = n.Parent)
        {
            if (n.IsLink || n.IsHeading)
                return false;
        }
        return true;
    }

    private static bool InSurroundings(VBufferNode node)
    {
        for (var n = node; n is not null; n = n.Parent)
        {
            if (SurroundingLandmarks.Contains(n.LandmarkType))
                return true;
        }
        return false;
    }
}
