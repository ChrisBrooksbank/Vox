using Vox.Core.Audio;
using Vox.Core.Buffer;
using Vox.Core.Input;

namespace Vox.Core.Navigation;

/// <summary>
/// Handles Browse-mode quick navigation single-letter key commands.
///
/// Supported commands:
///   NextHeading / PrevHeading  — H / Shift+H: next/prev heading any level
///   HeadingLevel1-6            — 1-6: next heading at specific level
///   PrevHeadingLevel1-6        — Shift+1-6: previous heading at specific level
///   NextLink / PrevLink        — K / Shift+K: next/prev link
///   NextLandmark / PrevLandmark— D / Shift+D: next/prev landmark
///   NextFormField / PrevFormField — F / Shift+F: next/prev form field
///   NextTable / PrevTable      — T / Shift+T: next/prev table (not yet indexed; plays boundary)
///   NextFocusable / PrevFocusable — Tab / Shift+Tab: next/prev focusable element
///   Next/Prev Button, Edit, ComboBox, CheckBox, RadioButton — B, E, C, X, R (Shift: previous)
///   Next/Prev List, ListItem, Graphic, BlockQuote, Frame, Separator, EmbeddedObject — L, I, G, Q, M, S, O
///   Next/Prev VisitedLink, UnvisitedLink — V, U
///   Next/Prev UnvisitedHeading — J: a heading the cursor hasn't been on in this document
///   Next/Prev NonLinkText, TextParagraph — N, P: text lines, found by <see cref="FindTextLine"/>
///   End/StartOfContainer — , / Shift+,: found by <see cref="FindContainerEdge"/>
///
/// Plays boundary.wav when no element is found and wrapping is disabled.
/// Plays wrap.wav when wrapping to the other end of the collection.
/// </summary>
public sealed class QuickNavHandler
{
    private readonly IAudioCuePlayer _audioCuePlayer;

    private VBufferDocument? _document;
    private VBufferNode? _currentNode;

    // Headings the cursor has been on in the current document (by runtime id), for J
    private readonly HashSet<string> _visitedHeadings = new();
    private string? _visitedHeadingsDocument;

    // The current document's text lines, built on first use (documents are immutable snapshots)
    private IReadOnlyList<TextLine>? _textLines;

    /// <summary>
    /// Current cursor node — updated after each navigation. Set externally after focus changes.
    /// A heading it is on (or in) counts as visited for J.
    /// </summary>
    public VBufferNode? CurrentNode
    {
        get => _currentNode;
        set
        {
            _currentNode = value;
            for (var n = value; n is not null; n = n.Parent)
            {
                if (n.IsHeading)
                    _visitedHeadings.Add(RuntimeIdKey(n));
            }
        }
    }

    /// <summary>The currently active document, or null if none has been set.</summary>
    public VBufferDocument? CurrentDocument => _document;

    /// <summary>When true the handler wraps from last to first (and first to last) element.</summary>
    public bool WrapEnabled { get; set; } = true;

    public QuickNavHandler(IAudioCuePlayer audioCuePlayer)
    {
        _audioCuePlayer = audioCuePlayer;
    }

    // -------------------------------------------------------------------------
    // Document management
    // -------------------------------------------------------------------------

    /// <summary>Sets (or replaces) the active VBufferDocument. Resets CurrentNode.</summary>
    public void SetDocument(VBufferDocument? document)
    {
        _document = document;
        _textLines = null;
        CurrentNode = null;

        // Visited headings belong to a page: an update of the same document keeps them
        if (document is not null)
        {
            var key = RuntimeIdKey(document.Root);
            if (key != _visitedHeadingsDocument)
            {
                _visitedHeadings.Clear();
                _visitedHeadingsDocument = key;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Command dispatch
    // -------------------------------------------------------------------------

    /// <summary>True if <paramref name="command"/> is a quick-navigation command handled by <see cref="Handle"/>.</summary>
    public static bool IsQuickNavCommand(NavigationCommand command) => command is
        NavigationCommand.NextHeading or NavigationCommand.PrevHeading or
        NavigationCommand.HeadingLevel1 or NavigationCommand.HeadingLevel2 or NavigationCommand.HeadingLevel3 or
        NavigationCommand.HeadingLevel4 or NavigationCommand.HeadingLevel5 or NavigationCommand.HeadingLevel6 or
        NavigationCommand.PrevHeadingLevel1 or NavigationCommand.PrevHeadingLevel2 or NavigationCommand.PrevHeadingLevel3 or
        NavigationCommand.PrevHeadingLevel4 or NavigationCommand.PrevHeadingLevel5 or NavigationCommand.PrevHeadingLevel6 or
        NavigationCommand.NextLink or NavigationCommand.PrevLink or
        NavigationCommand.NextLandmark or NavigationCommand.PrevLandmark or
        NavigationCommand.NextFormField or NavigationCommand.PrevFormField or
        NavigationCommand.NextTable or NavigationCommand.PrevTable or
        NavigationCommand.NextFocusable or NavigationCommand.PrevFocusable or
        NavigationCommand.NextButton or NavigationCommand.PrevButton or
        NavigationCommand.NextEdit or NavigationCommand.PrevEdit or
        NavigationCommand.NextComboBox or NavigationCommand.PrevComboBox or
        NavigationCommand.NextCheckBox or NavigationCommand.PrevCheckBox or
        NavigationCommand.NextRadioButton or NavigationCommand.PrevRadioButton or
        NavigationCommand.NextList or NavigationCommand.PrevList or
        NavigationCommand.NextListItem or NavigationCommand.PrevListItem or
        NavigationCommand.NextGraphic or NavigationCommand.PrevGraphic or
        NavigationCommand.NextBlockQuote or NavigationCommand.PrevBlockQuote or
        NavigationCommand.NextFrame or NavigationCommand.PrevFrame or
        NavigationCommand.NextSeparator or NavigationCommand.PrevSeparator or
        NavigationCommand.NextEmbeddedObject or NavigationCommand.PrevEmbeddedObject or
        NavigationCommand.NextVisitedLink or NavigationCommand.PrevVisitedLink or
        NavigationCommand.NextUnvisitedLink or NavigationCommand.PrevUnvisitedLink or
        NavigationCommand.NextUnvisitedHeading or NavigationCommand.PrevUnvisitedHeading;

    /// <summary>True for the commands that move by text line (N, P), handled by <see cref="FindTextLine"/>.</summary>
    public static bool IsTextLineCommand(NavigationCommand command) => command is
        NavigationCommand.NextNonLinkText or NavigationCommand.PrevNonLinkText or
        NavigationCommand.NextTextParagraph or NavigationCommand.PrevTextParagraph;

    /// <summary>
    /// What an element found by <paramref name="command"/> is called when it is announced
    /// ("list", "graphic"), for the kinds whose control type doesn't say it; null for the others.
    /// </summary>
    public static string? ElementKindName(NavigationCommand command) => command switch
    {
        NavigationCommand.NextList or NavigationCommand.PrevList => "list",
        NavigationCommand.NextListItem or NavigationCommand.PrevListItem => "list item",
        NavigationCommand.NextGraphic or NavigationCommand.PrevGraphic => "graphic",
        NavigationCommand.NextBlockQuote or NavigationCommand.PrevBlockQuote => "block quote",
        NavigationCommand.NextFrame or NavigationCommand.PrevFrame => "frame",
        NavigationCommand.NextSeparator or NavigationCommand.PrevSeparator => "separator",
        NavigationCommand.NextEmbeddedObject or NavigationCommand.PrevEmbeddedObject => "embedded object",
        _ => null,
    };

    /// <summary>
    /// Handles a quick-navigation command in Browse mode.
    /// Returns the node navigated to, or null if no match / no document.
    /// </summary>
    public VBufferNode? Handle(NavigationCommand command)
    {
        if (_document is null) return null;

        return command switch
        {
            NavigationCommand.NextHeading     => FindNext(_document.Headings, _ => true),
            NavigationCommand.PrevHeading     => FindPrev(_document.Headings, _ => true),

            NavigationCommand.HeadingLevel1   => FindNext(_document.Headings, n => n.HeadingLevel == 1),
            NavigationCommand.HeadingLevel2   => FindNext(_document.Headings, n => n.HeadingLevel == 2),
            NavigationCommand.HeadingLevel3   => FindNext(_document.Headings, n => n.HeadingLevel == 3),
            NavigationCommand.HeadingLevel4   => FindNext(_document.Headings, n => n.HeadingLevel == 4),
            NavigationCommand.HeadingLevel5   => FindNext(_document.Headings, n => n.HeadingLevel == 5),
            NavigationCommand.HeadingLevel6   => FindNext(_document.Headings, n => n.HeadingLevel == 6),

            NavigationCommand.PrevHeadingLevel1 => FindPrev(_document.Headings, n => n.HeadingLevel == 1),
            NavigationCommand.PrevHeadingLevel2 => FindPrev(_document.Headings, n => n.HeadingLevel == 2),
            NavigationCommand.PrevHeadingLevel3 => FindPrev(_document.Headings, n => n.HeadingLevel == 3),
            NavigationCommand.PrevHeadingLevel4 => FindPrev(_document.Headings, n => n.HeadingLevel == 4),
            NavigationCommand.PrevHeadingLevel5 => FindPrev(_document.Headings, n => n.HeadingLevel == 5),
            NavigationCommand.PrevHeadingLevel6 => FindPrev(_document.Headings, n => n.HeadingLevel == 6),

            NavigationCommand.NextLink        => FindNext(_document.Links, _ => true),
            NavigationCommand.PrevLink        => FindPrev(_document.Links, _ => true),

            NavigationCommand.NextLandmark    => FindNext(_document.Landmarks, _ => true),
            NavigationCommand.PrevLandmark    => FindPrev(_document.Landmarks, _ => true),

            NavigationCommand.NextFormField   => FindNext(_document.FormFields, _ => true),
            NavigationCommand.PrevFormField   => FindPrev(_document.FormFields, _ => true),

            NavigationCommand.NextTable       => FindNext(_document.Tables, _ => true),
            NavigationCommand.PrevTable       => FindPrev(_document.Tables, _ => true),

            NavigationCommand.NextFocusable   => FindNext(_document.FocusableElements, _ => true),
            NavigationCommand.PrevFocusable   => FindPrev(_document.FocusableElements, _ => true),

            NavigationCommand.NextButton      => FindNext(_document.Buttons, _ => true),
            NavigationCommand.PrevButton      => FindPrev(_document.Buttons, _ => true),
            NavigationCommand.NextEdit        => FindNext(_document.Edits, _ => true),
            NavigationCommand.PrevEdit        => FindPrev(_document.Edits, _ => true),
            NavigationCommand.NextComboBox    => FindNext(_document.ComboBoxes, _ => true),
            NavigationCommand.PrevComboBox    => FindPrev(_document.ComboBoxes, _ => true),
            NavigationCommand.NextCheckBox    => FindNext(_document.CheckBoxes, _ => true),
            NavigationCommand.PrevCheckBox    => FindPrev(_document.CheckBoxes, _ => true),
            NavigationCommand.NextRadioButton => FindNext(_document.RadioButtons, _ => true),
            NavigationCommand.PrevRadioButton => FindPrev(_document.RadioButtons, _ => true),

            NavigationCommand.NextList           => FindNext(_document.Lists, _ => true),
            NavigationCommand.PrevList           => FindPrev(_document.Lists, _ => true),
            NavigationCommand.NextListItem       => FindNext(_document.ListItems, _ => true),
            NavigationCommand.PrevListItem       => FindPrev(_document.ListItems, _ => true),
            NavigationCommand.NextGraphic        => FindNext(_document.Graphics, _ => true),
            NavigationCommand.PrevGraphic        => FindPrev(_document.Graphics, _ => true),
            NavigationCommand.NextBlockQuote     => FindNext(_document.BlockQuotes, _ => true),
            NavigationCommand.PrevBlockQuote     => FindPrev(_document.BlockQuotes, _ => true),
            NavigationCommand.NextFrame          => FindNext(_document.Frames, _ => true),
            NavigationCommand.PrevFrame          => FindPrev(_document.Frames, _ => true),
            NavigationCommand.NextSeparator      => FindNext(_document.Separators, _ => true),
            NavigationCommand.PrevSeparator      => FindPrev(_document.Separators, _ => true),
            NavigationCommand.NextEmbeddedObject => FindNext(_document.EmbeddedObjects, _ => true),
            NavigationCommand.PrevEmbeddedObject => FindPrev(_document.EmbeddedObjects, _ => true),

            NavigationCommand.NextVisitedLink    => FindNext(_document.Links, n => n.IsVisited),
            NavigationCommand.PrevVisitedLink    => FindPrev(_document.Links, n => n.IsVisited),
            NavigationCommand.NextUnvisitedLink  => FindNext(_document.Links, n => !n.IsVisited),
            NavigationCommand.PrevUnvisitedLink  => FindPrev(_document.Links, n => !n.IsVisited),

            NavigationCommand.NextUnvisitedHeading => FindNext(_document.Headings, n => !_visitedHeadings.Contains(RuntimeIdKey(n))),
            NavigationCommand.PrevUnvisitedHeading => FindPrev(_document.Headings, n => !_visitedHeadings.Contains(RuntimeIdKey(n))),

            _ => null,
        };
    }

    // -------------------------------------------------------------------------
    // Text lines (N, P)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Finds the next (or previous) text line for a text-line command, relative to the line
    /// containing <paramref name="offset"/>: for N, text that follows a block of links (the first
    /// line that isn't all links after one that is); for P, a paragraph of plain text (letters or
    /// digits outside links, headings, form controls and graphics). Lines are the buffer's
    /// '\n'-separated lines. Returns the line's start offset, or null (after the boundary cue).
    /// Plays the wrap cue when the search wraps.
    /// </summary>
    public int? FindTextLine(NavigationCommand command, int offset)
    {
        if (_document is null) return null;

        Func<TextLine, bool> predicate = command switch
        {
            NavigationCommand.NextNonLinkText or NavigationCommand.PrevNonLinkText => l => l.FollowsLinks,
            NavigationCommand.NextTextParagraph or NavigationCommand.PrevTextParagraph => l => l.IsPlainText,
            _ => _ => false,
        };
        bool forward = command is NavigationCommand.NextNonLinkText or NavigationCommand.NextTextParagraph;

        var lines = _textLines ??= BuildTextLines(_document);
        string text = _document.FlatText;
        int lineStart = offset <= 0 ? 0 : text.LastIndexOf('\n', Math.Min(offset, text.Length) - 1) + 1;

        // The lines before and after the current one, searched in order, then the rest (wrapping)
        var after = lines.Where(l => l.Start > lineStart);
        var before = lines.Where(l => l.Start < lineStart);
        var (ahead, wrapped) = forward ? (after, before) : (before.Reverse(), after.Reverse());

        foreach (var line in ahead)
        {
            if (predicate(line))
                return line.Start;
        }
        if (WrapEnabled)
        {
            foreach (var line in wrapped)
            {
                if (predicate(line))
                {
                    _audioCuePlayer.Play("wrap");
                    return line.Start;
                }
            }
        }

        _audioCuePlayer.Play("boundary");
        return null;
    }

    /// <summary>A line of the buffer that has text, classified for N and P.</summary>
    private readonly record struct TextLine(int Start, bool FollowsLinks, bool IsPlainText);

    private static IReadOnlyList<TextLine> BuildTextLines(VBufferDocument document)
    {
        string text = document.FlatText;
        var lines = new List<TextLine>();
        bool previousIsLinks = false;
        int start = 0;
        while (start < text.Length)
        {
            int end = text.IndexOf('\n', start);
            if (end < 0) end = text.Length;

            bool hasText = false, hasNonLinkText = false, isPlainText = false;
            // Walk the line node by node (each character belongs to at most one node's text)
            int pos = start;
            while (pos < end)
            {
                var node = document.FindNodeAtOffset(pos);
                int segmentEnd = node is null ? pos + 1 : Math.Min(Math.Max(node.TextRange.End, pos + 1), end);
                var segment = text.AsSpan(pos, segmentEnd - pos);
                if (!segment.IsWhiteSpace())
                {
                    hasText = true;
                    var (inLink, plain) = Classify(node);
                    hasNonLinkText |= !inLink;
                    if (plain && !isPlainText)
                    {
                        foreach (var ch in segment)
                        {
                            if (char.IsLetterOrDigit(ch)) { isPlainText = true; break; }
                        }
                    }
                }
                pos = segmentEnd;
            }

            if (hasText)
            {
                lines.Add(new TextLine(start, previousIsLinks && hasNonLinkText, isPlainText));
                previousIsLinks = !hasNonLinkText;
            }
            start = end + 1;
        }
        return lines;
    }

    /// <summary>Whether a node's text is inside a link, and whether it is plain text (for P).</summary>
    private static (bool InLink, bool Plain) Classify(VBufferNode? node)
    {
        bool inLink = false, plain = true;
        for (var n = node; n is not null; n = n.Parent)
        {
            if (n.IsLink)
                inLink = true;
            if (n.IsLink || n.IsHeading || FormControls.IsFormField(n.ControlType, n.AriaRole)
                || PageElements.IsGraphic(n) || PageElements.IsSeparator(n))
                plain = false;
        }
        return (inLink, plain);
    }

    // -------------------------------------------------------------------------
    // Containers (, and Shift+,)
    // -------------------------------------------------------------------------

    /// <summary>
    /// True for the elements , and Shift+, move past or back to the start of: lists, tables,
    /// landmarks, block quotes and frames.
    /// </summary>
    public static bool IsContainer(VBufferNode node) =>
        PageElements.IsList(node) || VBufferDocument.IsTable(node) || node.IsLandmark
        || PageElements.IsBlockQuote(node) || PageElements.IsFrame(node);

    /// <summary>
    /// Finds the innermost container around <paramref name="offset"/> and where , (end: the
    /// first line after it) or Shift+, (start: its first text) goes. Shift+, at a container's
    /// start goes to the start of the one around it; , from a container that ends the document
    /// goes past the one around it. Returns null (after the boundary cue) when there is none.
    /// Updates <see cref="CurrentNode"/> on success.
    /// </summary>
    public (VBufferNode Container, int Offset)? FindContainerEdge(bool end, int offset)
    {
        if (_document is null) return null;

        string text = _document.FlatText;
        var node = _document.FindNodeAtOffset(offset) ?? CurrentNode;
        for (var n = node; n is not null && !ReferenceEquals(n, _document.Root); n = n.Parent)
        {
            if (!IsContainer(n))
                continue;

            if (end)
            {
                int after = SubtreeTextEnd(n);
                if (after < text.Length && text[after] == '\n')
                    after++;
                if (after < text.Length)
                {
                    CurrentNode = _document.FindNodeAtOffset(after) ?? CurrentNode;
                    return (n, after);
                }
            }
            else if (n.TextRange.Start < offset)
            {
                CurrentNode = n;
                return (n, n.TextRange.Start);
            }
        }

        _audioCuePlayer.Play("boundary");
        return null;
    }

    /// <summary>The exclusive end of the text of <paramref name="node"/> and its descendants.</summary>
    private static int SubtreeTextEnd(VBufferNode node)
    {
        int end = node.TextRange.End;
        foreach (var child in node.Children)
            end = Math.Max(end, SubtreeTextEnd(child));
        return end;
    }

    private static string RuntimeIdKey(VBufferNode node) => string.Join(",", node.UIARuntimeId);

    // -------------------------------------------------------------------------
    // Navigation helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Finds the next node after <see cref="CurrentNode"/> in <paramref name="collection"/>
    /// that satisfies <paramref name="predicate"/>. Updates <see cref="CurrentNode"/> on success.
    /// </summary>
    private VBufferNode? FindNext(IReadOnlyList<VBufferNode> collection, Func<VBufferNode, bool> predicate)
    {
        if (collection.Count == 0)
        {
            _audioCuePlayer.Play("boundary");
            return null;
        }

        int startIndex = IndexAfterCurrent(collection);

        // Search forward from startIndex to end
        for (int i = startIndex; i < collection.Count; i++)
        {
            if (predicate(collection[i]))
            {
                CurrentNode = collection[i];
                return CurrentNode;
            }
        }

        // Wrap from beginning up to (but not including) startIndex
        if (WrapEnabled)
        {
            for (int i = 0; i < startIndex; i++)
            {
                if (predicate(collection[i]))
                {
                    _audioCuePlayer.Play("wrap");
                    CurrentNode = collection[i];
                    return CurrentNode;
                }
            }
        }

        _audioCuePlayer.Play("boundary");
        return null;
    }

    /// <summary>
    /// Finds the previous node before <see cref="CurrentNode"/> in <paramref name="collection"/>
    /// that satisfies <paramref name="predicate"/>. Updates <see cref="CurrentNode"/> on success.
    /// </summary>
    private VBufferNode? FindPrev(IReadOnlyList<VBufferNode> collection, Func<VBufferNode, bool> predicate)
    {
        if (collection.Count == 0)
        {
            _audioCuePlayer.Play("boundary");
            return null;
        }

        int endIndex = IndexBeforeCurrent(collection);

        // Search backward from endIndex to 0 (skipping the elements the cursor is inside)
        for (int i = endIndex; i >= 0; i--)
        {
            if (predicate(collection[i]) && !IsAncestorOfCurrent(collection[i]))
            {
                CurrentNode = collection[i];
                return CurrentNode;
            }
        }

        // Wrap from end down to (but not including) endIndex
        if (WrapEnabled)
        {
            for (int i = collection.Count - 1; i > endIndex; i--)
            {
                if (predicate(collection[i]))
                {
                    _audioCuePlayer.Play("wrap");
                    CurrentNode = collection[i];
                    return CurrentNode;
                }
            }
        }

        _audioCuePlayer.Play("boundary");
        return null;
    }

    // -------------------------------------------------------------------------
    // Index-finding helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the collection index to start a forward search from: the first item after
    /// <see cref="CurrentNode"/> in document order. Items can contain one another (a navigation
    /// landmark inside main), so this goes by document order, not by the item the cursor is in.
    /// Returns collection.Count if no forward starting point exists (triggers wrap).
    /// </summary>
    private int IndexAfterCurrent(IReadOnlyList<VBufferNode> collection)
    {
        if (CurrentNode is null) return 0;

        int currentId = CurrentNode.Id;
        for (int i = 0; i < collection.Count; i++)
        {
            if (collection[i].Id > currentId)
                return i;
        }

        return collection.Count; // triggers wrap
    }

    /// <summary>
    /// Returns the collection index to start a backward search from: the last item before
    /// <see cref="CurrentNode"/> in document order that doesn't contain it (so Shift+H inside a
    /// heading's text finds the previous heading, and Shift+D inside main finds the landmark
    /// before the cursor, even one nested in main).
    /// Returns -1 if no backward starting point exists (triggers wrap).
    /// </summary>
    private int IndexBeforeCurrent(IReadOnlyList<VBufferNode> collection)
    {
        if (CurrentNode is null) return collection.Count - 1;

        int currentId = CurrentNode.Id;
        for (int i = collection.Count - 1; i >= 0; i--)
        {
            if (collection[i].Id < currentId && !IsAncestorOfCurrent(collection[i]))
                return i;
        }

        return -1; // triggers wrap
    }

    private bool IsAncestorOfCurrent(VBufferNode node)
    {
        for (var n = CurrentNode?.Parent; n is not null; n = n.Parent)
        {
            if (ReferenceEquals(n, node))
                return true;
        }
        return false;
    }
}
