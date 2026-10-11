namespace Vox.Core.AppModules.Word;

/// <summary>Composes what is said for Word: quick navigation, tracked changes and comments, the location and table cells.</summary>
public static class WordAnnouncer
{
    /// <summary>"heading", "table" ... as said in "No next heading".</summary>
    public static string KindName(WordElementKind kind) => kind switch
    {
        WordElementKind.Heading => "heading",
        WordElementKind.Table => "table",
        WordElementKind.List => "list",
        WordElementKind.Comment => "comment",
        WordElementKind.Revision => "tracked change",
        _ => "spelling error",
    };

    /// <summary>What there is none of in that direction: "No next heading".</summary>
    public static string NotFound(WordElementKind kind, bool forward) =>
        $"No {(forward ? "next" : "previous")} {KindName(kind)}";

    /// <summary>The element quick navigation moved to.</summary>
    public static string Element(WordElement element) => element.Kind switch
    {
        WordElementKind.Heading => Join(Text(element.Text), element.Level > 0 ? $"heading level {element.Level}" : "heading"),
        WordElementKind.Table => Join($"table with {Count(element.Rows, "row")}, {Count(element.Columns, "column")}", Text(element.Text)),
        WordElementKind.List => Join($"list with {Count(element.Items, "item")}", Text(element.Text)),
        WordElementKind.Comment when element.Comment is { } comment => Comment(comment),
        WordElementKind.Revision when element.Revision is { } revision => Revision(revision),
        WordElementKind.SpellingError => Join(Text(element.Text), "misspelled"),
        _ => Text(element.Text),
    };

    /// <summary>"Comment by Ann: check this figure".</summary>
    public static string Comment(WordComment comment) =>
        $"Comment by {Author(comment.Author)}: {Text(comment.Text)}";

    /// <summary>"Insertion by Bob: new text", "Deletion by Bob: old text", "Formatting change by Bob".</summary>
    public static string Revision(WordRevision revision)
    {
        var what = RevisionTypeName(revision.Type);
        var text = string.IsNullOrWhiteSpace(revision.Text) || revision.Type is not (1 or 2)
            ? string.Empty
            : $": {revision.Text.Trim()}";
        return $"{what} by {Author(revision.Author)}{text}";
    }

    /// <summary>A name for Word's WdRevisionType.</summary>
    public static string RevisionTypeName(int type) => type switch
    {
        1 => "Insertion",
        2 => "Deletion",
        3 => "Property change",
        4 => "Paragraph number change",
        5 => "Field display change",
        6 => "Reconciled change",
        7 => "Conflict",
        8 => "Style change",
        9 => "Replaced",
        10 => "Paragraph property change",
        11 => "Table property change",
        12 => "Section property change",
        13 => "Style definition change",
        14 => "Moved from",
        15 => "Moved to",
        16 => "Cell inserted",
        17 => "Cell deleted",
        18 => "Cells merged",
        _ => "Change",
    };

    /// <summary>
    /// What is said when the caret moves into comments or tracked changes it wasn't in before
    /// (<paramref name="previous"/>); null when it entered none.
    /// </summary>
    public static string? Entered(WordCaretContext context, WordCaretContext previous)
    {
        var parts = context.Revisions.Where(r => !previous.Revisions.Contains(r)).Select(Revision)
            .Concat(context.Comments.Where(c => !previous.Comments.Contains(c)).Select(Comment))
            .ToList();
        return parts.Count == 0 ? null : string.Join(". ", parts);
    }

    /// <summary>"Page 3 of 10, section 1, line 12, column 5".</summary>
    public static string Location(WordLocation location)
    {
        var parts = new List<string>();
        if (location.Page > 0)
            parts.Add(location.Pages > 0 ? $"Page {location.Page} of {location.Pages}" : $"Page {location.Page}");
        if (location.Section > 0)
            parts.Add($"section {location.Section}");
        if (location.Line > 0)
            parts.Add($"line {location.Line}");
        if (location.Column > 0)
            parts.Add($"column {location.Column}");
        return parts.Count == 0 ? "Location not available" : string.Join(", ", parts);
    }

    /// <summary>
    /// A table cell after moving: the header of the column (or row) moved into, then the text and
    /// coordinates. Headers come from the first row and column, and aren't said in those.
    /// </summary>
    public static string Cell(WordTableCell cell, WordTableMove move)
    {
        var parts = new List<string>();
        bool horizontal = move is WordTableMove.Left or WordTableMove.Right;
        var header = horizontal
            ? (cell.Row > 1 ? cell.ColumnHeader : null)
            : (cell.Column > 1 ? cell.RowHeader : null);
        if (!string.IsNullOrWhiteSpace(header))
            parts.Add(header.Trim());
        parts.Add(Text(cell.Text));
        parts.Add($"row {cell.Row}, column {cell.Column}");
        return string.Join(", ", parts);
    }

    /// <summary>Said when there is no cell that way.</summary>
    public static string TableEdge(WordTableMove move) => move switch
    {
        WordTableMove.Left => "Start of row",
        WordTableMove.Right => "End of row",
        WordTableMove.Up => "Top of column",
        _ => "Bottom of column",
    };

    private static string Text(string text)
    {
        // Word ends paragraphs and cells with \r and \a
        var trimmed = text.Replace('\a', ' ').Replace('\r', ' ').Replace('\v', ' ').Trim();
        return trimmed.Length == 0 ? "blank" : trimmed;
    }

    private static string Author(string author) => string.IsNullOrWhiteSpace(author) ? "unknown author" : author.Trim();

    private static string Count(int n, string singular) => n == 1 ? $"1 {singular}" : $"{n} {singular}s";

    private static string Join(params string[] parts) => string.Join(", ", parts.Where(p => p.Length > 0));
}
