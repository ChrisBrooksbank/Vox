using System.Text;
using System.Text.RegularExpressions;
using Vox.Core.Buffer;

namespace Vox.Core.Ai;

/// <summary>
/// "Ask the page": the page's text as a describer is given it, each line and each element in it
/// numbered ("[12] Opening hours"), so an answer can name where it comes from ("GO TO: [12]"); and
/// the answer read back, with the number it names checked against the page that was sent (a
/// number the page doesn't have is ignored). Immutable: made on the pipeline thread, read from any.
/// </summary>
public sealed partial class PageQuestion
{
    private readonly Dictionary<int, (VBufferNode Node, string Text)> _lines;

    private PageQuestion(string context, Dictionary<int, (VBufferNode Node, string Text)> lines)
    {
        Context = context;
        _lines = lines;
    }

    /// <summary>The numbered text of the page, as sent.</summary>
    public string Context { get; }

    /// <summary>How many lines were numbered.</summary>
    public int LineCount => _lines.Count;

    /// <summary>
    /// The page's text, line by line (blank lines left out), each line numbered, and each link,
    /// control, heading and graphic inside a line numbered where it starts.
    /// </summary>
    public static PageQuestion For(VBufferDocument document)
    {
        var text = document.FlatText;
        int from = 0, to = text.Length;
        if (document.ModalScope is { } scope)
        {
            // While a modal dialog is open the rest of the page is inert: only the dialog is asked about
            (from, to) = (scope.Start, Math.Min(scope.End, text.Length));
        }

        // Where numbers go: the start of each line, and of each element worth moving to
        var marks = new SortedDictionary<int, VBufferNode>();
        foreach (var node in document.AllNodes)
        {
            if (node.Parent is null || !IsElement(node))
                continue;
            var (start, end) = VBufferDocument.SubtreeSpan(node);
            start = SkipSpace(text, start, end);
            if (start >= from && start < to && start < end)
                marks.TryAdd(start, node);
        }
        for (int start = from; start < to;)
        {
            int end = text.IndexOf('\n', start, to - start);
            if (end < 0)
                end = to;
            int first = SkipSpace(text, start, end);
            if (first < end && !marks.ContainsKey(first) && document.FindNodeAtOffset(first) is { } node)
                marks[first] = node;
            start = end + 1;
        }

        var lines = new Dictionary<int, (VBufferNode, string)>();
        var context = new StringBuilder();
        var offsets = marks.Keys.ToList();
        int number = 0;
        for (int i = 0; i < offsets.Count && context.Length <= AiPrompts.MaxContextLength; i++)
        {
            int start = offsets[i];
            int next = i + 1 < offsets.Count ? offsets[i + 1] : to;
            int lineEnd = text.IndexOf('\n', start, next - start);
            var piece = text[start..(lineEnd < 0 ? next : lineEnd)].Trim();
            if (piece.Length == 0)
                continue;
            number++;
            lines[number] = (marks[start], piece);
            // A number inside a line follows on the same line; a new line starts a new one
            bool startsLine = start == from || IsLineStart(text, start);
            if (startsLine && context.Length > 0)
                context.Append('\n');
            else if (context.Length > 0)
                context.Append(' ');
            context.Append('[').Append(number).Append("] ").Append(piece);
        }
        if (context.Length > 0)
            context.Append('\n');
        return new PageQuestion(context.ToString(), lines);
    }

    // What an answer may send the browse cursor to, besides a line
    private static bool IsElement(VBufferNode node) =>
        node.IsLink || node.IsFocusable || node.IsHeading || PageElements.IsGraphic(node);

    private static bool IsLineStart(string text, int offset)
    {
        for (int i = offset - 1; i >= 0; i--)
        {
            if (text[i] == '\n')
                return true;
            if (!char.IsWhiteSpace(text[i]))
                return false;
        }
        return true;
    }

    private static int SkipSpace(string text, int start, int end)
    {
        end = Math.Min(end, text.Length);
        while (start < end && char.IsWhiteSpace(text[start]))
            start++;
        return start;
    }

    /// <summary>
    /// The answer to say (the "GO TO" line and any line numbers taken out) and the element the
    /// answer named, when it named a line this page has.
    /// </summary>
    public (string Text, VBufferNode? Target, string? TargetText) Read(string answer)
    {
        VBufferNode? target = null;
        string? targetText = null;
        var kept = new List<string>();
        foreach (var raw in answer.ReplaceLineEndings("\n").Split('\n'))
        {
            var match = GoToLine().Match(raw);
            if (match.Success)
            {
                if (target is null && int.TryParse(match.Groups[1].Value, out var number) && _lines.TryGetValue(number, out var line))
                    (target, targetText) = line;
                continue;
            }
            kept.Add(LineNumber().Replace(raw, string.Empty).TrimEnd());
        }
        var text = string.Join("\n", kept).Trim();
        text = MultipleSpaces().Replace(text, " ");
        return (text, target, targetText);
    }

    /// <summary>One step of a "Guide me" answer: what to do, and the element it is at when it named a line this page has.</summary>
    public sealed record GuideStep(string Text, VBufferNode? Target, string? TargetText);

    /// <summary>
    /// A "Guide me" answer read back: the text to say (line numbers and "GO TO" taken out), its
    /// numbered steps with the element each named (a number the page doesn't have is ignored),
    /// and the model's "Not sure" note, if any.
    /// </summary>
    public (string Text, IReadOnlyList<GuideStep> Steps, string? Uncertainty) ReadSteps(string answer)
    {
        var steps = new List<GuideStep>();
        var kept = new List<string>();
        string? note = null;
        foreach (var raw in answer.ReplaceLineEndings("\n").Split('\n'))
        {
            VBufferNode? target = null;
            string? targetText = null;
            var line = raw;
            var tail = GoToTail().Match(line);
            if (tail.Success)
            {
                if (int.TryParse(tail.Groups[1].Value, out var number) && _lines.TryGetValue(number, out var found))
                    (target, targetText) = found;
                line = line[..tail.Index];
            }
            line = MultipleSpaces().Replace(LineNumber().Replace(line, string.Empty), " ").TrimEnd();
            if (line.Trim().Length == 0)
                continue;
            kept.Add(line);
            if (line.TrimStart().StartsWith("Not sure", StringComparison.OrdinalIgnoreCase))
                note ??= line.Trim();
            else if (StepStart().IsMatch(line))
                steps.Add(new GuideStep(StepStart().Replace(line, string.Empty, 1).Trim(), target, targetText));
        }
        return (string.Join("\n", kept), steps, note);
    }

    [GeneratedRegex(@"\s*GO\s*TO\s*:?\s*\[?\s*(\d+)\s*\]?\s*\.?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex GoToTail();

    [GeneratedRegex(@"^\s*\d+\s*[.)]\s*")]
    private static partial Regex StepStart();

    [GeneratedRegex(@"^\s*GO\s*TO\s*:?\s*\[?\s*(\d+)\s*\]?\s*\.?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex GoToLine();

    [GeneratedRegex(@"\s?\[\d+\]")]
    private static partial Regex LineNumber();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex MultipleSpaces();

}
