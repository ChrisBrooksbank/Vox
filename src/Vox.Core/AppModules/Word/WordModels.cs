namespace Vox.Core.AppModules.Word;

/// <summary>The kinds of things Word's quick navigation moves between.</summary>
public enum WordElementKind
{
    Heading,
    Table,
    List,
    Comment,
    Revision,
    SpellingError,
}

/// <summary>Where the caret is: page, section, line and column (all 1-based, 0 when Word didn't say).</summary>
public sealed record WordLocation(int Page, int Section, int Line, int Column, int Pages = 0);

/// <summary>A comment: its author and text.</summary>
public sealed record WordComment(string Author, string Text);

/// <summary>A tracked change: Word's WdRevisionType and its author and text.</summary>
public sealed record WordRevision(int Type, string Author, string Text);

/// <summary>
/// What quick navigation reached: its kind, its text (a heading's, a table's first cell, a list's
/// first item, a misspelled word), and per kind a heading's level, a table's size, a list's item
/// count, a comment or a revision.
/// </summary>
public sealed record WordElement(
    WordElementKind Kind,
    string Text,
    int Level = 0,
    int Rows = 0,
    int Columns = 0,
    int Items = 0,
    WordComment? Comment = null,
    WordRevision? Revision = null);

/// <summary>The comments and tracked changes at the caret.</summary>
public sealed record WordCaretContext(IReadOnlyList<WordComment> Comments, IReadOnlyList<WordRevision> Revisions)
{
    public static readonly WordCaretContext Empty = new([], []);

    public bool IsEmpty => Comments.Count == 0 && Revisions.Count == 0;
}

/// <summary>A table cell the caret is in: its text, place, the table's size and its headers (first row's and first column's text).</summary>
public sealed record WordTableCell(
    string Text,
    int Row,
    int Column,
    int Rows,
    int Columns,
    string? ColumnHeader = null,
    string? RowHeader = null);

/// <summary>Directions table navigation moves in.</summary>
public enum WordTableMove
{
    Left,
    Right,
    Up,
    Down,
}
