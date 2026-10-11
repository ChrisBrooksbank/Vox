using System.Runtime.CompilerServices;
using System.Text;
using Vox.Core.Buffer;
using Vox.Core.Configuration;

namespace Vox.Core.Braille;

/// <summary>
/// Builds the braille lines: a focused object with its context (the ancestors around it, as the
/// focus-context setting says), the caret's line in an edit control, the browse cursor's line of a
/// page with the role labels of the elements on it ("Home lnk"), and the review cursor's line.
/// Pure: the braille controller feeds it and shows what it returns.
/// </summary>
public sealed class BraillePresenter
{
    private readonly BrailleLabels _labels;

    // The context of the last focus line, for the "changed context" mode
    private IReadOnlyList<BrailleObject> _lastAncestors = [];

    public BraillePresenter(BrailleLabels? labels = null)
    {
        _labels = labels ?? BrailleLabels.Default;
    }

    public BrailleLabels Labels => _labels;

    /// <summary>
    /// The line for a newly focused object. <paramref name="ancestors"/> run from the outermost
    /// (the window) to the focus's parent; unnamed structural ones are left out. Fill display shows
    /// as much context as fits before the object; scroll back only starts at the object; changed
    /// context shows only the ancestors from the first that differs from the last focus's.
    /// </summary>
    public BrailleLine ForFocus(BrailleObject focus, IReadOnlyList<BrailleObject> ancestors, BrailleFocusContext mode)
    {
        var context = ancestors.Where(a => _labels.Describe(a).Length > 0 && !IsLayout(a)).ToList();
        if (mode == BrailleFocusContext.ChangedContext)
        {
            var same = 0;
            while (same < context.Count && same < _lastAncestors.Count && context[same].IsSameObject(_lastAncestors[same]))
                same++;
            _lastAncestors = context;
            context = context.Skip(same).ToList();
        }
        else
        {
            _lastAncestors = context;
        }

        var text = new StringBuilder();
        foreach (var ancestor in context)
            text.Append(_labels.Describe(ancestor)).Append(' ');
        var anchor = text.Length;
        text.Append(_labels.Describe(focus));
        var line = text.ToString().TrimEnd();
        return new BrailleLine
        {
            Kind = BrailleLineKind.Focus,
            Text = line,
            AnchorOffset = Math.Min(anchor, line.Length),
            AnchorMode = mode == BrailleFocusContext.ScrollBackOnly ? BrailleAnchorMode.AtAnchor : BrailleAnchorMode.FillBeforeAnchor,
            SourceEnd = line.Length,
        };
    }

    /// <summary>Forgets the last focus's context (another application, a dialog closed).</summary>
    public void ResetContext() => _lastAncestors = [];

    /// <summary>
    /// The caret's line in an edit control: <paramref name="lineText"/> with the caret at
    /// <paramref name="caretInLine"/> and the selection's part of the line. Offsets are the line's.
    /// </summary>
    public static BrailleLine ForEditLine(string lineText, int? caretInLine, (int Start, int End)? selectionInLine)
    {
        var text = OneLine(lineText, out var cut);
        return new BrailleLine
        {
            Kind = BrailleLineKind.EditLine,
            Text = text,
            CursorOffset = caretInLine is { } caret ? Math.Clamp(caret, 0, text.Length) : null,
            Selection = Clip(selectionInLine, text.Length),
            SourceEnd = cut ? text.Length : lineText.Length,
        };
    }

    /// <summary>The review cursor's line (offsets are the line's).</summary>
    public static BrailleLine ForReviewLine(string lineText, int? cursorInLine) =>
        ForEditLine(lineText, cursorInLine, null) with { Kind = BrailleLineKind.Review };

    /// <summary>
    /// The browse cursor's line of a page: the text from <paramref name="lineStart"/> to
    /// <paramref name="lineEnd"/>, with each link, button, form field, heading and graphic on it
    /// followed by its role and states ("Home lnk", "Subscribe chk ( )"). Source offsets are the
    /// page's, so routing keys and the cursor map straight to the buffer.
    /// </summary>
    public BrailleLine ForBrowseLine(VBufferDocument document, int lineStart, int lineEnd, int cursor, (int Start, int End)? selection = null)
    {
        var flat = document.FlatText;
        lineStart = Math.Clamp(lineStart, 0, flat.Length);
        lineEnd = Math.Clamp(lineEnd, lineStart, flat.Length);

        // Labels go after the element's last character on this line
        var labelsAt = new SortedDictionary<int, List<(string Label, int Source)>>();
        foreach (var node in LabelledNodes(document))
        {
            var (start, end) = VBufferDocument.SubtreeSpan(node);
            if (end <= lineStart || start >= lineEnd || (start < lineStart && end > lineEnd))
                continue;
            var label = Annotation(node);
            if (label.Length == 0)
                continue;
            var at = Math.Min(end, lineEnd);
            if (!labelsAt.TryGetValue(at, out var list))
                labelsAt[at] = list = [];
            list.Add((label, Math.Max(start, lineStart)));
        }

        var text = new StringBuilder(lineEnd - lineStart + 16);
        var map = new List<int>(lineEnd - lineStart + 16);
        void AddLabels(int at)
        {
            if (!labelsAt.TryGetValue(at, out var list))
                return;
            foreach (var (label, source) in list)
            {
                text.Append(' ');
                map.Add(source);
                foreach (var c in label)
                {
                    text.Append(c);
                    map.Add(source);
                }
            }
        }

        for (var i = lineStart; i < lineEnd; i++)
        {
            AddLabels(i);
            var c = flat[i];
            text.Append(c is '\n' or '\r' or '\t' ? ' ' : c);
            map.Add(i);
        }
        AddLabels(lineEnd);

        // A label added before any text (an empty element at the line's start) leaves a leading space
        var line = new BrailleLine
        {
            Kind = BrailleLineKind.BrowseLine,
            Text = text.ToString(),
            SourceOffsets = map.ToArray(),
            SourceStart = lineStart,
            SourceEnd = lineEnd,
        };
        int? cursorOffset = cursor >= lineStart && cursor <= lineEnd ? line.TextOffsetOf(cursor) : null;
        (int, int)? selected = null;
        if (selection is { } sel && sel.End > lineStart && sel.Start < lineEnd)
        {
            var from = line.TextOffsetOf(Math.Max(sel.Start, lineStart)) ?? 0;
            var to = line.TextOffsetOf(Math.Min(sel.End, lineEnd)) ?? line.Text.Length;
            selected = (from, Math.Max(from, to));
        }
        return line with { CursorOffset = cursorOffset, Selection = selected };
    }

    /// <summary>What braille adds after an element on a page: its role and states, not its name or value (the page shows those).</summary>
    public string Annotation(VBufferNode node)
    {
        var obj = BrailleObject.From(node) with { Name = string.Empty, Value = string.Empty, PositionInSet = 0, SizeOfSet = 0, Level = 0 };
        return _labels.Describe(obj);
    }

    private static readonly ConditionalWeakTable<VBufferDocument, HashSet<VBufferNode>> LabelledCache = new();

    /// <summary>The elements braille labels on a page: links, buttons, form fields, headings and graphics.</summary>
    private static HashSet<VBufferNode> LabelledNodes(VBufferDocument document) =>
        LabelledCache.GetValue(document, d =>
        [
            .. d.Links, .. d.Buttons, .. d.FormFields, .. d.Headings, .. d.Graphics,
        ]);

    /// <summary>Unnamed groups and panes carry nothing to show.</summary>
    private static bool IsLayout(BrailleObject obj) =>
        string.IsNullOrWhiteSpace(obj.Name) && obj.ControlType is "Group" or "Pane" or "Custom" or "Text" && obj.LandmarkType.Length == 0;

    /// <summary>The first line of <paramref name="text"/>, tabs as spaces; <paramref name="cut"/> when there was more.</summary>
    private static string OneLine(string text, out bool cut)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        cut = end >= 0;
        var line = cut ? text[..end] : text;
        return line.Replace('\t', ' ');
    }

    private static (int Start, int End)? Clip((int Start, int End)? range, int length)
    {
        if (range is not { } r)
            return null;
        var start = Math.Clamp(r.Start, 0, length);
        var end = Math.Clamp(r.End, start, length);
        return end > start ? (start, end) : null;
    }
}
