using Vox.Core.Localization;

namespace Vox.Core.AppModules.Word;

/// <summary>Composes what is said for Word: quick navigation, tracked changes and comments, the location and table cells.</summary>
public static class WordAnnouncer
{
    /// <summary>"heading", "table" ... as said in "No next heading".</summary>
    public static string KindName(WordElementKind kind) => kind switch
    {
        WordElementKind.Heading => VoxStrings.Get("Word.Kind.Heading"),
        WordElementKind.Table => VoxStrings.Get("ControlType.Table"),
        WordElementKind.List => VoxStrings.Get("ControlType.List"),
        WordElementKind.Comment => VoxStrings.Get("Word.Kind.Comment"),
        WordElementKind.Revision => VoxStrings.Get("Word.Kind.Revision"),
        _ => VoxStrings.Get("Word.Kind.SpellingError"),
    };

    /// <summary>What there is none of in that direction: "No next heading".</summary>
    public static string NotFound(WordElementKind kind, bool forward) =>
        forward ? VoxStrings.Format("Word.NoNext", KindName(kind)) : VoxStrings.Format("Word.NoPrevious", KindName(kind));

    /// <summary>The element quick navigation moved to.</summary>
    public static string Element(WordElement element) => element.Kind switch
    {
        WordElementKind.Heading => Join(Text(element.Text), element.Level > 0 ? VoxStrings.Format("Announce.HeadingLevel", element.Level) : VoxStrings.Get("Word.Kind.Heading")),
        WordElementKind.Table => Join(VoxStrings.Format("Word.TableWith", Rows(element.Rows), Columns(element.Columns)), Text(element.Text)),
        WordElementKind.List => Join(VoxStrings.Format("Word.ListWith", element.Items == 1 ? VoxStrings.Get("AppModule.OneItem") : VoxStrings.Format("AppModule.Items", element.Items)), Text(element.Text)),
        WordElementKind.Comment when element.Comment is { } comment => Comment(comment),
        WordElementKind.Revision when element.Revision is { } revision => Revision(revision),
        WordElementKind.SpellingError => Join(Text(element.Text), VoxStrings.Get("Word.Misspelled")),
        _ => Text(element.Text),
    };

    /// <summary>"Comment by Ann: check this figure".</summary>
    public static string Comment(WordComment comment) =>
        VoxStrings.Format("Word.CommentBy", Author(comment.Author), Text(comment.Text));

    /// <summary>"Insertion by Bob: new text", "Deletion by Bob: old text", "Formatting change by Bob".</summary>
    public static string Revision(WordRevision revision)
    {
        var what = RevisionTypeName(revision.Type);
        return string.IsNullOrWhiteSpace(revision.Text) || revision.Type is not (1 or 2)
            ? VoxStrings.Format("Word.RevisionBy", what, Author(revision.Author))
            : VoxStrings.Format("Word.RevisionByWithText", what, Author(revision.Author), revision.Text.Trim());
    }

    /// <summary>A name for Word's WdRevisionType.</summary>
    public static string RevisionTypeName(int type) => type switch
    {
        1 => VoxStrings.Get("Word.Revision.Insertion"),
        2 => VoxStrings.Get("Word.Revision.Deletion"),
        3 => VoxStrings.Get("Word.Revision.PropertyChange"),
        4 => VoxStrings.Get("Word.Revision.ParagraphNumberChange"),
        5 => VoxStrings.Get("Word.Revision.FieldDisplayChange"),
        6 => VoxStrings.Get("Word.Revision.ReconciledChange"),
        7 => VoxStrings.Get("Word.Revision.Conflict"),
        8 => VoxStrings.Get("Word.Revision.StyleChange"),
        9 => VoxStrings.Get("Word.Revision.Replaced"),
        10 => VoxStrings.Get("Word.Revision.ParagraphPropertyChange"),
        11 => VoxStrings.Get("Word.Revision.TablePropertyChange"),
        12 => VoxStrings.Get("Word.Revision.SectionPropertyChange"),
        13 => VoxStrings.Get("Word.Revision.StyleDefinitionChange"),
        14 => VoxStrings.Get("Word.Revision.MovedFrom"),
        15 => VoxStrings.Get("Word.Revision.MovedTo"),
        16 => VoxStrings.Get("Word.Revision.CellInserted"),
        17 => VoxStrings.Get("Word.Revision.CellDeleted"),
        18 => VoxStrings.Get("Word.Revision.CellsMerged"),
        _ => VoxStrings.Get("Word.Revision.Change"),
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
            parts.Add(location.Pages > 0 ? VoxStrings.Format("Word.PageOf", location.Page, location.Pages) : VoxStrings.Format("Word.Page", location.Page));
        if (location.Section > 0)
            parts.Add(VoxStrings.Format("Word.Section", location.Section));
        if (location.Line > 0)
            parts.Add(VoxStrings.Format("Word.Line", location.Line));
        if (location.Column > 0)
            parts.Add(VoxStrings.Format("Word.Column", location.Column));
        return parts.Count == 0 ? VoxStrings.Get("Word.LocationUnavailable") : string.Join(", ", parts);
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
        parts.Add(VoxStrings.Format("Word.RowColumn", cell.Row, cell.Column));
        return string.Join(", ", parts);
    }

    /// <summary>Said when there is no cell that way.</summary>
    public static string TableEdge(WordTableMove move) => move switch
    {
        WordTableMove.Left => VoxStrings.Get("Word.StartOfRow"),
        WordTableMove.Right => VoxStrings.Get("Word.EndOfRow"),
        WordTableMove.Up => VoxStrings.Get("Word.TopOfColumn"),
        _ => VoxStrings.Get("Word.BottomOfColumn"),
    };

    private static string Text(string text)
    {
        // Word ends paragraphs and cells with \r and \a
        var trimmed = text.Replace('\a', ' ').Replace('\r', ' ').Replace('\v', ' ').Trim();
        return trimmed.Length == 0 ? VoxStrings.Get("AppModule.Blank") : trimmed;
    }

    private static string Author(string author) => string.IsNullOrWhiteSpace(author) ? VoxStrings.Get("Word.UnknownAuthor") : author.Trim();

    private static string Rows(int n) => n == 1 ? VoxStrings.Get("AppModule.OneRow") : VoxStrings.Format("AppModule.Rows", n);

    private static string Columns(int n) => n == 1 ? VoxStrings.Get("AppModule.OneColumn") : VoxStrings.Format("AppModule.Columns", n);

    private static string Join(params string[] parts) => string.Join(", ", parts.Where(p => p.Length > 0));
}
