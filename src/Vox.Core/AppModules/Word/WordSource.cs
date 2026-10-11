using Vox.Core.AppModules.Office;

namespace Vox.Core.AppModules.Word;

/// <summary>Where quick navigation got to: <see cref="Element"/> is null when there is none that way.</summary>
public sealed record WordMoveResult(WordElement? Element);

/// <summary>A table move: not in a table, at the table's edge (<see cref="Cell"/> null), or the cell moved to.</summary>
public sealed record WordTableResult(bool InTable, WordTableCell? Cell);

/// <summary>Reads and moves around the active Word document. Every method gives null when Word can't answer.</summary>
public interface IWordSource
{
    Task<WordLocation?> LocationAsync(IntPtr window);

    /// <summary>Moves the caret to the next (or previous) element of <paramref name="kind"/> and describes it.</summary>
    Task<WordMoveResult?> MoveToAsync(IntPtr window, WordElementKind kind, bool forward);

    /// <summary>The comments and tracked changes at the caret.</summary>
    Task<WordCaretContext?> CaretContextAsync(IntPtr window);

    /// <summary>Moves the caret to the neighbouring table cell.</summary>
    Task<WordTableResult?> MoveInTableAsync(IntPtr window, WordTableMove move);
}

/// <summary>
/// <see cref="IWordSource"/> through Word's object model (late bound, on the Office thread).
/// Headings and revisions are found with Word's own GoTo and NextRevision; tables, lists, comments
/// and spelling errors by position in their collections (Word's Lists aren't in document order).
/// </summary>
public sealed class WordObjectModelSource : IWordSource
{
    // Quick navigation may walk a long document's collections
    private static readonly TimeSpan MoveTimeout = TimeSpan.FromSeconds(3);

    // WdInformation
    private const int ActiveEndSectionNumber = 2;
    private const int ActiveEndPageNumber = 3;
    private const int NumberOfPagesInDocument = 4;
    private const int FirstCharacterColumnNumber = 9;
    private const int FirstCharacterLineNumber = 10;
    private const int WithInTable = 12;
    private const int StartOfRangeRowNumber = 13;
    private const int StartOfRangeColumnNumber = 16;

    // WdGoToItem and WdGoToDirection
    private const int GoToHeading = 11;
    private const int GoToNext = 2;
    private const int GoToPrevious = 3;

    private readonly IOfficeObjectModel _objectModel;

    public WordObjectModelSource(IOfficeObjectModel objectModel)
    {
        _objectModel = objectModel;
    }

    public Task<WordLocation?> LocationAsync(IntPtr window) =>
        _objectModel.RunAsync<WordLocation>(OfficeApp.Word, window, app =>
        {
            object selection = app.Selection;
            return new WordLocation(
                Info(selection, ActiveEndPageNumber), Info(selection, ActiveEndSectionNumber),
                Info(selection, FirstCharacterLineNumber), Info(selection, FirstCharacterColumnNumber),
                Info(selection, NumberOfPagesInDocument));
        });

    public Task<WordMoveResult?> MoveToAsync(IntPtr window, WordElementKind kind, bool forward) =>
        _objectModel.RunAsync<WordMoveResult>(OfficeApp.Word, window, app => new WordMoveResult(Move((object)app, kind, forward)), MoveTimeout);

    public Task<WordCaretContext?> CaretContextAsync(IntPtr window) =>
        _objectModel.RunAsync<WordCaretContext>(OfficeApp.Word, window, app => CaretContext((object)app));

    public Task<WordTableResult?> MoveInTableAsync(IntPtr window, WordTableMove move) =>
        _objectModel.RunAsync<WordTableResult>(OfficeApp.Word, window, app => MoveInTable((object)app, move));

    private static int Info(object selection, int what)
    {
        try { return Convert.ToInt32(((dynamic)selection).Information(what)); }
        catch { return 0; }
    }

    private static WordElement? Move(object application, WordElementKind kind, bool forward)
    {
        dynamic app = application;
        dynamic document = app.ActiveDocument;
        dynamic selection = app.Selection;
        int caret = (int)selection.Start;
        switch (kind)
        {
            case WordElementKind.Heading:
                return MoveToHeading((object)document, (object)selection, caret, forward);
            case WordElementKind.Revision:
                dynamic? revision = forward ? selection.NextRevision(false) : selection.PreviousRevision(false);
                if (revision is null)
                    return null;
                return new WordElement(kind, string.Empty, Revision: Revision((object)revision));
        }

        dynamic? found = null;
        int foundStart = forward ? int.MaxValue : int.MinValue;
        foreach (var (item, start, end) in Items((object)document, kind))
        {
            bool beyond = forward ? start > caret : end <= caret && start < caret;
            if (beyond && (forward ? start < foundStart : start > foundStart))
            {
                found = item;
                foundStart = start;
            }
        }
        if (found is null)
            return null;
        document.Range(foundStart, foundStart).Select();
        return kind switch
        {
            WordElementKind.Table => new WordElement(kind, (string)found.Range.Cells(1).Range.Text,
                Rows: (int)found.Rows.Count, Columns: (int)found.Columns.Count),
            WordElementKind.List => new WordElement(kind, (string)found.Range.Paragraphs(1).Range.Text,
                Items: (int)found.ListParagraphs.Count),
            WordElementKind.Comment => new WordElement(kind, string.Empty,
                Comment: new WordComment((string)found.Author, (string)found.Range.Text)),
            _ => new WordElement(kind, (string)found.Text),
        };
    }

    /// <summary>The tables, lists, comments (at their scope) or misspelled words of the document, with where each starts and ends.</summary>
    private static IEnumerable<(dynamic Item, int Start, int End)> Items(object doc, WordElementKind kind)
    {
        dynamic document = doc;
        dynamic collection = kind switch
        {
            WordElementKind.Table => document.Tables,
            WordElementKind.List => document.Lists,
            WordElementKind.Comment => document.Comments,
            _ => document.SpellingErrors,
        };
        int count = (int)collection.Count;
        for (int i = 1; i <= count; i++)
        {
            dynamic item = collection.Item(i);
            dynamic range = kind switch
            {
                WordElementKind.Comment => item.Scope,
                WordElementKind.SpellingError => item,
                _ => item.Range,
            };
            yield return (item, (int)range.Start, (int)range.End);
        }
    }

    private static WordElement? MoveToHeading(object doc, object sel, int caret, bool forward)
    {
        dynamic document = doc;
        dynamic selection = sel;
        // Word leaves the selection alone when there is no heading that way
        dynamic range = selection.GoTo(GoToHeading, forward ? GoToNext : GoToPrevious);
        int start = (int)range.Start;
        if (forward ? start <= caret : start >= caret)
        {
            document.Range(caret, caret).Select();
            return null;
        }
        dynamic paragraph = range.Paragraphs(1);
        int level = (int)paragraph.OutlineLevel;
        return new WordElement(WordElementKind.Heading, (string)paragraph.Range.Text, Level: level is >= 1 and <= 9 ? level : 0);
    }

    private static WordRevision Revision(object rev)
    {
        dynamic revision = rev;
        return new((int)revision.Type, (string)revision.Author, (string)revision.Range.Text);
    }

    private static WordCaretContext CaretContext(object application)
    {
        dynamic app = application;
        dynamic document = app.ActiveDocument;
        int caret = (int)app.Selection.Start;
        int end = (int)document.Content.End;
        dynamic here = document.Range(caret, Math.Min(caret + 1, end));

        var revisions = new List<WordRevision>();
        dynamic hereRevisions = here.Revisions;
        int revisionCount = (int)hereRevisions.Count;
        for (int i = 1; i <= revisionCount; i++)
            revisions.Add(Revision((object)hereRevisions.Item(i)));

        var comments = new List<WordComment>();
        foreach (var (item, start, scopeEnd) in Items((object)document, WordElementKind.Comment))
        {
            if (caret >= start && (caret < scopeEnd || caret == start))
                comments.Add(new WordComment((string)item.Author, (string)item.Range.Text));
        }
        return new WordCaretContext(comments, revisions);
    }

    private static WordTableResult MoveInTable(object application, WordTableMove move)
    {
        dynamic app = application;
        object sel = app.Selection;
        dynamic selection = sel;
        if (!(bool)selection.Information(WithInTable))
            return new WordTableResult(false, null);
        int row = Info(sel, StartOfRangeRowNumber);
        int column = Info(sel, StartOfRangeColumnNumber);
        dynamic table = selection.Tables(1);
        int rows = (int)table.Rows.Count;
        int columns = (int)table.Columns.Count;
        var (targetRow, targetColumn) = move switch
        {
            WordTableMove.Left => (row, column - 1),
            WordTableMove.Right => (row, column + 1),
            WordTableMove.Up => (row - 1, column),
            _ => (row + 1, column),
        };
        if (targetRow < 1 || targetRow > rows || targetColumn < 1 || targetColumn > columns)
            return new WordTableResult(true, null);

        dynamic cell;
        try { cell = table.Cell(targetRow, targetColumn); }
        catch { return new WordTableResult(true, null); } // merged away
        cell.Select();
        selection.Collapse(1); // wdCollapseStart
        return new WordTableResult(true, new WordTableCell(
            (string)cell.Range.Text, targetRow, targetColumn, rows, columns,
            ColumnHeader: CellText((object)table, 1, targetColumn), RowHeader: CellText((object)table, targetRow, 1)));
    }

    private static string? CellText(object table, int row, int column)
    {
        try { return ((string)((dynamic)table).Cell(row, column).Range.Text).Replace('\a', ' ').Replace('\r', ' ').Trim(); }
        catch { return null; }
    }
}
