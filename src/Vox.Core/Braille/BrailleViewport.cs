using Vox.Core.Configuration;

namespace Vox.Core.Braille;

/// <summary>How a line is drawn: cursor, selection and word wrap.</summary>
public sealed record BrailleViewOptions
{
    public BrailleCursorShape CursorShape { get; init; } = BrailleCursorShape.Underline;
    public bool ShowSelection { get; init; } = true;
    public bool WordWrap { get; init; } = true;
    public bool ExpandWordAtCursor { get; init; } = true;

    public static BrailleViewOptions From(VoxSettings settings) => new()
    {
        CursorShape = settings.BrailleCursorShape,
        ShowSelection = settings.BrailleShowSelection,
        WordWrap = settings.BrailleWordWrap,
        ExpandWordAtCursor = settings.BrailleExpandWordAtCursor,
    };

    /// <summary>The dots the cursor raises.</summary>
    public byte CursorDots => CursorShape switch
    {
        BrailleCursorShape.Dot8 => BrailleDots.Dot8,
        BrailleCursorShape.AllDots => 0xFF,
        _ => BrailleDots.Dots78,
    };
}

/// <summary>
/// The part of a translated <see cref="BrailleLine"/> the display shows (its window), panned
/// left and right a display's width at a time, never splitting a word with word wrap on. Draws
/// the cursor and the selection (dots 7–8) into the cells and maps routing keys back to the line.
/// Not thread-safe: the braille controller serializes calls.
/// </summary>
public sealed class BrailleViewport
{
    private readonly IBrailleTranslator _translator;
    private BrailleViewOptions _options = new();
    private int _cellCount;

    public BrailleViewport(IBrailleTranslator translator)
    {
        _translator = translator;
    }

    /// <summary>The line shown, or null before the first.</summary>
    public BrailleLine? Line { get; private set; }

    /// <summary>The whole line translated.</summary>
    public BrailleTranslation Translation { get; private set; } = BrailleTranslation.Empty;

    /// <summary>The first cell of the line in the window.</summary>
    public int WindowStart { get; private set; }

    /// <summary>The cell just after the last one of the line in the window.</summary>
    public int WindowEnd => WindowEndFrom(WindowStart);

    /// <summary>The display's width the window was laid out for.</summary>
    public int CellCount => _cellCount;

    /// <summary>
    /// Shows <paramref name="line"/> on a display of <paramref name="cellCount"/> cells. With
    /// <paramref name="keepWindow"/> the window stays where it was if it still shows the cursor
    /// (typing on the same line); otherwise it moves to the cursor, or to the line's anchor.
    /// </summary>
    public void Show(BrailleLine line, int cellCount, BrailleViewOptions options, bool keepWindow = false)
    {
        var previousStart = WindowStart;
        _options = options;
        _cellCount = Math.Max(cellCount, 0);
        Line = line;
        // Direction marks in Arabic and Hebrew text aren't shown (Text.BidiText)
        Translation = Text.BidiText.Translate(_translator, line.Text, line.CursorOffset,
            options.ExpandWordAtCursor && line.CursorOffset is not null);

        if (keepWindow && previousStart < Math.Max(Translation.Cells.Length, 1) && CursorVisibleFrom(previousStart))
        {
            WindowStart = previousStart;
            return;
        }
        WindowStart = InitialStart();
    }

    /// <summary>Changes the display width (another display connected), keeping the cursor or anchor shown.</summary>
    public void Resize(int cellCount)
    {
        if (cellCount == _cellCount || Line is null)
            return;
        _cellCount = Math.Max(cellCount, 0);
        WindowStart = InitialStart();
    }

    /// <summary>Moves the window a display's width right. False (and no move) at the end of the line.</summary>
    public bool PanRight()
    {
        var end = WindowEnd;
        if (_cellCount == 0 || end >= Translation.Cells.Length)
            return false;
        WindowStart = SkipBlanks(end);
        return true;
    }

    /// <summary>Moves the window a display's width left. False (and no move) at the start of the line.</summary>
    public bool PanLeft()
    {
        if (_cellCount == 0 || WindowStart <= 0)
            return false;
        WindowStart = PreviousStart(WindowStart);
        return true;
    }

    /// <summary>Shows the end of the line (after panning left into it from the next line).</summary>
    public void ShowLineEnd()
    {
        var cells = Translation.Cells;
        var start = 0;
        while (true)
        {
            var end = WindowEndFrom(start);
            if (end >= cells.Length || _cellCount == 0)
                break;
            start = SkipBlanks(end);
        }
        WindowStart = start;
    }

    /// <summary>The cell under the cursor within the window, or null when the cursor isn't in it.</summary>
    public int? CursorCellInWindow
    {
        get
        {
            if (Translation.CursorCell is not { } cursor)
                return null;
            var end = WindowEnd;
            // A cursor at the end of the line sits on the blank cell after it, if that is on the display
            var lastCell = cursor >= Translation.Cells.Length ? WindowStart + _cellCount : end;
            return cursor >= WindowStart && cursor < lastCell && cursor - WindowStart < _cellCount
                ? cursor - WindowStart
                : null;
        }
    }

    /// <summary>
    /// The cells to write: the window, padded to the display's width, with the selection's dots
    /// 7–8 and, when <paramref name="cursorVisible"/>, the cursor drawn in.
    /// </summary>
    public byte[] Render(bool cursorVisible)
    {
        var output = new byte[_cellCount];
        if (Line is null || _cellCount == 0)
            return output;
        var cells = Translation.Cells;
        var start = WindowStart;
        var end = WindowEnd;
        for (var i = start; i < end; i++)
            output[i - start] = cells[i];

        if (_options.ShowSelection && Line.Selection is { } selection && selection.End > selection.Start)
        {
            var first = Translation.CellOf(selection.Start);
            var last = Math.Max(first, Translation.CellOf(selection.End));
            for (var cell = Math.Max(first, start); cell < Math.Min(last, end); cell++)
                output[cell - start] |= BrailleDots.Dots78;
        }

        if (cursorVisible && CursorCellInWindow is { } cursorCell)
            output[cursorCell] |= _options.CursorDots;
        return output;
    }

    /// <summary>
    /// The offset in the line's text shown at display cell <paramref name="cell"/> (a routing key),
    /// or null for a cell past the end of what the window shows.
    /// </summary>
    public int? TextOffsetAtCell(int cell)
    {
        if (Line is null || cell < 0 || cell >= _cellCount)
            return null;
        var lineCell = WindowStart + cell;
        var end = WindowEnd;
        if (lineCell < end)
            return Translation.OffsetOf(lineCell);
        // The blank cell just after the line's end: the caret's place at the end of the line
        return lineCell == Translation.Cells.Length && end == Translation.Cells.Length ? Line.Text.Length : null;
    }

    private int InitialStart()
    {
        if (Line is null || _cellCount == 0)
            return 0;
        if (Translation.CursorCell is { } cursor)
            return WindowContaining(cursor);

        var cells = Translation.Cells;
        var anchor = Math.Min(Translation.CellOf(Line.AnchorOffset), cells.Length);
        switch (Line.AnchorMode)
        {
            case BrailleAnchorMode.AtAnchor:
                return anchor >= cells.Length ? WindowContaining(Math.Max(cells.Length - 1, 0)) : anchor;
            case BrailleAnchorMode.FillBeforeAnchor:
                var start = Math.Min(Math.Max(cells.Length - _cellCount, 0), anchor);
                // Context starts at a word, not halfway through one
                while (start > 0 && start < anchor && cells[start - 1] != 0)
                    start++;
                return start;
            default:
                return 0;
        }
    }

    /// <summary>The start of the window holding <paramref name="cell"/>, panning from the line's start.</summary>
    private int WindowContaining(int cell)
    {
        var start = 0;
        while (true)
        {
            var end = WindowEndFrom(start);
            // The cursor after the last cell shows in the last window, when there is room
            if (cell < end || end >= Translation.Cells.Length)
            {
                if (cell >= end && cell - start >= _cellCount)
                    return cell;
                return start;
            }
            var next = SkipBlanks(end);
            // The cursor on a blank panning skips (between two windows): a window starting at it
            if (cell < next)
                return cell;
            start = next;
        }
    }

    private bool CursorVisibleFrom(int start)
    {
        if (Translation.CursorCell is not { } cursor)
            return true;
        var end = WindowEndFrom(start);
        return cursor >= start && (cursor < end || (end >= Translation.Cells.Length && cursor - start < _cellCount));
    }

    /// <summary>The end of the window starting at <paramref name="start"/>: a display's width, backed up to a word's start with word wrap.</summary>
    private int WindowEndFrom(int start)
    {
        var cells = Translation.Cells;
        var end = Math.Min(start + _cellCount, cells.Length);
        if (!_options.WordWrap || end >= cells.Length || end <= start)
            return end;
        // The next cell continues a word cut by the window's edge: end the window before that word
        if (cells[end] != 0 && cells[end - 1] != 0)
        {
            var wordStart = end - 1;
            while (wordStart > start && cells[wordStart - 1] != 0)
                wordStart--;
            if (wordStart > start)
                return wordStart;
        }
        return end;
    }

    /// <summary>Panning right starts at the next word, not at the blanks before it.</summary>
    private int SkipBlanks(int cell)
    {
        var cells = Translation.Cells;
        var start = cell;
        while (start < cells.Length && cells[start] == 0)
            start++;
        return start < cells.Length ? start : cell;
    }

    /// <summary>The start of the window before the one starting at <paramref name="start"/>.</summary>
    private int PreviousStart(int start)
    {
        // The window that pans right onto start: the last window start, panning from the line's
        // start, before it (so panning left and right go through the same windows)
        var previous = 0;
        var candidate = 0;
        while (true)
        {
            var end = WindowEndFrom(candidate);
            if (end >= start || end >= Translation.Cells.Length || end <= candidate)
                return candidate < start ? candidate : previous;
            previous = candidate;
            candidate = SkipBlanks(end);
            if (candidate >= start)
                return previous;
        }
    }
}
