using Vox.Core.Buffer;
using Vox.Core.Input;

namespace Vox.Core.Navigation;

/// <summary>
/// The outcome of a table navigation command: the cell moved to and what to say, or that the
/// cursor isn't in a table, or that it is at the table's edge (the cell is unchanged).
/// </summary>
public sealed record TableMoveResult(TableCell? Cell, string? Text, bool NotInTable = false, bool AtEdge = false);

/// <summary>
/// Moves cell by cell through the table around the browse cursor (Ctrl+Alt+arrows, Ctrl+Alt+Home
/// / End) over the document's <see cref="TableModel"/>s.
///
/// Says the table's dimensions on entering a table, the row or column number when it changes
/// (with its row or column headers when those changed), then the cell's text. Moving down or up
/// through a cell spanning several columns keeps the column the user came from.
/// Pure: the caller moves the cursor and speaks; call on the pipeline thread.
/// </summary>
public sealed class TableNavigator
{
    // Where the user last was: table, logical row and column (inside a spanning cell's slots)
    private TableModel? _table;
    private int _row;
    private int _column;
    private string _rowHeaders = string.Empty;
    private string _columnHeaders = string.Empty;

    /// <summary>True for the cell-to-cell commands.</summary>
    public static bool IsTableCommand(NavigationCommand command) => command is
        NavigationCommand.TableNextColumn or NavigationCommand.TablePrevColumn or
        NavigationCommand.TableNextRow or NavigationCommand.TablePrevRow or
        NavigationCommand.TableFirstCell or NavigationCommand.TableLastCell;

    /// <summary>Forgets the last position, so the next command announces the table afresh.</summary>
    public void Reset()
    {
        _table = null;
        _rowHeaders = string.Empty;
        _columnHeaders = string.Empty;
    }

    /// <summary>
    /// Moves from the cell containing <paramref name="current"/> as the command says.
    /// </summary>
    public TableMoveResult Move(VBufferDocument document, VBufferNode? current, NavigationCommand command)
    {
        if (document.FindTableCell(current) is not { } found)
            return new TableMoveResult(null, null, NotInTable: true);

        var (table, cell) = found;
        bool entering = !IsSameTable(table);

        // The logical position: the remembered slot while still inside the same cell, else its origin
        int row = cell.Row, column = cell.Column;
        if (!entering && cell.CoversRow(_row) && cell.CoversColumn(_column))
            (row, column) = (_row, _column);

        (int Row, int Column)? target = command switch
        {
            NavigationCommand.TableNextColumn => (row, cell.Column + cell.ColumnSpan),
            NavigationCommand.TablePrevColumn => (row, cell.Column - 1),
            NavigationCommand.TableNextRow => (cell.Row + cell.RowSpan, column),
            NavigationCommand.TablePrevRow => (cell.Row - 1, column),
            NavigationCommand.TableFirstCell => (0, 0),
            NavigationCommand.TableLastCell => LastCell(table),
            _ => null,
        };
        if (target is not { } t || table.CellAt(t.Row, t.Column) is not { } next)
            return new TableMoveResult(null, null, AtEdge: true);

        bool rowChanged = entering || t.Row != row;
        bool columnChanged = entering || t.Column != column;
        var parts = new List<string>();
        if (entering)
            parts.Add(Dimensions(table));

        var rowHeaders = HeaderText(document, table.RowHeadersFor(next));
        var columnHeaders = HeaderText(document, table.ColumnHeadersFor(next));
        if (rowChanged)
        {
            if (rowHeaders.Length > 0 && (entering || rowHeaders != _rowHeaders))
                parts.Add(rowHeaders);
            parts.Add($"row {t.Row + 1}");
        }
        if (columnChanged)
        {
            if (columnHeaders.Length > 0 && (entering || columnHeaders != _columnHeaders))
                parts.Add(columnHeaders);
            parts.Add($"column {t.Column + 1}");
        }
        var text = CellText(document, next.Node);
        parts.Add(text.Length > 0 ? text : "blank");

        _table = table;
        (_row, _column) = t;
        _rowHeaders = rowHeaders;
        _columnHeaders = columnHeaders;
        return new TableMoveResult(next, string.Join(", ", parts));
    }

    /// <summary>"table with 3 rows and 2 columns".</summary>
    public static string Dimensions(TableModel table) =>
        $"table with {Count(table.RowCount, "row")} and {Count(table.ColumnCount, "column")}";

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    // The same table across document updates is the same table node
    private bool IsSameTable(TableModel table) =>
        _table is not null && (ReferenceEquals(_table, table)
            || (table.Table.UIARuntimeId.Length > 0
                && _table.Table.UIARuntimeId.AsSpan().SequenceEqual(table.Table.UIARuntimeId)));

    /// <summary>The last row's last cell (the bottom-right slot, or the last cell of a short last row).</summary>
    private static (int, int) LastCell(TableModel table)
    {
        int row = table.RowCount - 1;
        for (int c = table.ColumnCount - 1; c >= 0; c--)
        {
            if (table.CellAt(row, c) is not null)
                return (row, c);
        }
        return (row, 0);
    }

    private static string HeaderText(VBufferDocument document, IReadOnlyList<TableCell> headers) =>
        string.Join(" ", headers.Select(h => CellText(document, h.Node)).Where(s => s.Length > 0));

    /// <summary>The text of a node's whole subtree, on one line.</summary>
    public static string CellText(VBufferDocument document, VBufferNode node)
    {
        int start = node.TextRange.Start;
        int end = SubtreeEnd(node);
        if (end <= start || start >= document.FlatText.Length)
            return string.Empty;
        var text = document.FlatText.Substring(start, Math.Min(end, document.FlatText.Length) - start);
        return string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static int SubtreeEnd(VBufferNode node)
    {
        int end = node.TextRange.End;
        foreach (var child in node.Children)
            end = Math.Max(end, SubtreeEnd(child));
        return end;
    }
}
