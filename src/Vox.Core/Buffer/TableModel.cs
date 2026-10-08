namespace Vox.Core.Buffer;

/// <summary>What a table cell is: an ordinary data cell, or a column or row header.</summary>
public enum TableCellKind
{
    Data,
    ColumnHeader,
    RowHeader,
}

/// <summary>
/// One cell of a <see cref="TableModel"/>: its node, the row and column it starts at (0-based)
/// and how many rows and columns it spans.
/// </summary>
public sealed record TableCell(VBufferNode Node, int Row, int Column, int RowSpan, int ColumnSpan, TableCellKind Kind)
{
    /// <summary>True when the cell covers the given row.</summary>
    public bool CoversRow(int row) => row >= Row && row < Row + RowSpan;

    /// <summary>True when the cell covers the given column.</summary>
    public bool CoversColumn(int column) => column >= Column && column < Column + ColumnSpan;

    public bool IsHeader => Kind != TableCellKind.Data;
}

/// <summary>
/// The grid of a table in the virtual buffer: its cells placed by row and column (with row and
/// column spans laid out as HTML does), its header cells, its dimensions, and whether it looks
/// like a layout table rather than a data table.
///
/// Rows are ARIA rows (Chromium and Firefox report every &lt;tr&gt; with the row role, inside
/// rowgroups for &lt;thead&gt;/&lt;tbody&gt;); cells are cell/gridcell/columnheader/rowheader
/// (or UIA HeaderItem/DataItem in a row). Nested tables belong to their own model.
/// </summary>
public sealed class TableModel
{
    private readonly TableCell?[,] _grid;
    private readonly Dictionary<VBufferNode, TableCell> _byNode;

    /// <summary>The table (or grid) node.</summary>
    public VBufferNode Table { get; }

    /// <summary>Number of rows (including rows only reached by row spans).</summary>
    public int RowCount { get; }

    /// <summary>Number of columns: the widest row, counting column spans.</summary>
    public int ColumnCount { get; }

    /// <summary>All cells in document order.</summary>
    public IReadOnlyList<TableCell> Cells { get; }

    /// <summary>
    /// True for a table that probably only lays the page out: role presentation/none, a single
    /// row or column, or no header cells and no name (caption). ARIA grids are never layout tables.
    /// </summary>
    public bool IsLayoutTable { get; }

    private TableModel(VBufferNode table, List<TableCell> cells, int rowCount, int columnCount)
    {
        Table = table;
        Cells = cells;
        RowCount = rowCount;
        ColumnCount = columnCount;
        _grid = new TableCell?[rowCount, columnCount];
        _byNode = new Dictionary<VBufferNode, TableCell>(cells.Count);
        foreach (var cell in cells)
        {
            _byNode[cell.Node] = cell;
            for (int r = cell.Row; r < cell.Row + cell.RowSpan; r++)
                for (int c = cell.Column; c < cell.Column + cell.ColumnSpan; c++)
                    _grid[r, c] ??= cell;
        }
        IsLayoutTable = DetectLayout();
    }

    /// <summary>
    /// The cell covering a row and column (a spanning cell covers every slot it spans), or null
    /// when out of range or no cell is there (a short row).
    /// </summary>
    public TableCell? CellAt(int row, int column) =>
        row >= 0 && row < RowCount && column >= 0 && column < ColumnCount ? _grid[row, column] : null;

    /// <summary>The cell a node is, or is inside (the innermost cell of this table), or null.</summary>
    public TableCell? CellContaining(VBufferNode node)
    {
        for (var n = node; n is not null && n != Table; n = n.Parent)
        {
            if (_byNode.TryGetValue(n, out var cell))
                return cell;
        }
        return null;
    }

    /// <summary>
    /// The column header cells above a cell, top to bottom (not the cell itself). With
    /// <paramref name="headerRow"/> (set by the user for a table without header markup), the
    /// cells of that row are its column headers instead, for cells below it.
    /// </summary>
    public IReadOnlyList<TableCell> ColumnHeadersFor(TableCell cell, int? headerRow = null)
    {
        var headers = new List<TableCell>();
        if (headerRow is { } manual)
        {
            if (manual < 0 || manual >= RowCount || cell.Row <= manual)
                return headers;
            for (int c = cell.Column; c < cell.Column + cell.ColumnSpan; c++)
            {
                if (_grid[manual, c] is { } header && header != cell && !headers.Contains(header))
                    headers.Add(header);
            }
            return headers;
        }
        for (int r = 0; r < cell.Row; r++)
        {
            for (int c = cell.Column; c < cell.Column + cell.ColumnSpan; c++)
            {
                if (_grid[r, c] is { Kind: TableCellKind.ColumnHeader } header
                    && header != cell && !headers.Contains(header))
                    headers.Add(header);
            }
        }
        return headers;
    }

    /// <summary>
    /// The row header cells left of a cell, left to right (not the cell itself). With
    /// <paramref name="headerColumn"/> (set by the user), the cells of that column are its row
    /// headers instead, for cells right of it.
    /// </summary>
    public IReadOnlyList<TableCell> RowHeadersFor(TableCell cell, int? headerColumn = null)
    {
        var headers = new List<TableCell>();
        if (headerColumn is { } manual)
        {
            if (manual < 0 || manual >= ColumnCount || cell.Column <= manual)
                return headers;
            for (int r = cell.Row; r < cell.Row + cell.RowSpan; r++)
            {
                if (_grid[r, manual] is { } header && header != cell && !headers.Contains(header))
                    headers.Add(header);
            }
            return headers;
        }
        for (int c = 0; c < cell.Column; c++)
        {
            for (int r = cell.Row; r < cell.Row + cell.RowSpan; r++)
            {
                if (_grid[r, c] is { Kind: TableCellKind.RowHeader } header
                    && header != cell && !headers.Contains(header))
                    headers.Add(header);
            }
        }
        return headers;
    }

    /// <summary>
    /// Builds the model of a table node, or returns null when it isn't a table or has no cells.
    /// </summary>
    public static TableModel? Build(VBufferNode table)
    {
        if (!VBufferDocument.IsTable(table))
            return null;

        var rows = new List<VBufferNode>();
        CollectRows(table, rows);

        var cells = new List<TableCell>();
        // Slots taken by row spans from earlier rows: row -> occupied columns
        var occupied = new List<HashSet<int>>();
        int columnCount = 0;

        for (int r = 0; r < rows.Count; r++)
        {
            while (occupied.Count <= r) occupied.Add(new HashSet<int>());
            int column = 0;
            var rowCells = new List<VBufferNode>();
            CollectCells(rows[r], rowCells);
            foreach (var node in rowCells)
            {
                while (occupied[r].Contains(column)) column++;
                int rowSpan = Math.Max(1, node.RowSpan);
                int columnSpan = Math.Max(1, node.ColumnSpan);
                for (int rr = r; rr < r + rowSpan; rr++)
                {
                    while (occupied.Count <= rr) occupied.Add(new HashSet<int>());
                    for (int cc = column; cc < column + columnSpan; cc++)
                        occupied[rr].Add(cc);
                }
                cells.Add(new TableCell(node, r, column, rowSpan, columnSpan, KindOf(node)));
                column += columnSpan;
                columnCount = Math.Max(columnCount, column);
            }
        }

        if (cells.Count == 0)
            return null;

        // A row span past the last row is clipped to the table, as browsers do
        int rowCount = rows.Count;
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (cell.Row + cell.RowSpan > rowCount)
                cells[i] = cell with { RowSpan = rowCount - cell.Row };
        }

        return new TableModel(table, cells, rowCount, columnCount);
    }

    /// <summary>The cell kind of a node by ARIA role, or UIA HeaderItem for a column header.</summary>
    private static TableCellKind KindOf(VBufferNode node) => Role(node) switch
    {
        "columnheader" => TableCellKind.ColumnHeader,
        "rowheader" => TableCellKind.RowHeader,
        _ when node.ControlType == "HeaderItem" => TableCellKind.ColumnHeader,
        _ => TableCellKind.Data,
    };

    private static bool IsRow(VBufferNode node) => Role(node) == "row";

    private static bool IsCell(VBufferNode node) =>
        Role(node) is "cell" or "gridcell" or "columnheader" or "rowheader"
        || (string.IsNullOrEmpty(Role(node)) && node.ControlType is "HeaderItem" or "DataItem");

    /// <summary>Rows in document order, through row groups but not into nested tables or cells.</summary>
    private static void CollectRows(VBufferNode parent, List<VBufferNode> rows)
    {
        foreach (var child in parent.Children)
        {
            if (VBufferDocument.IsTable(child) || IsCell(child))
                continue;
            if (IsRow(child))
                rows.Add(child);
            else
                CollectRows(child, rows);
        }
    }

    /// <summary>A row's cells in document order, not inside nested tables or rows.</summary>
    private static void CollectCells(VBufferNode parent, List<VBufferNode> cells)
    {
        foreach (var child in parent.Children)
        {
            if (VBufferDocument.IsTable(child) || IsRow(child))
                continue;
            if (IsCell(child))
                cells.Add(child);
            else
                CollectCells(child, cells);
        }
    }

    private bool DetectLayout()
    {
        var role = Role(Table);
        if (role is "grid" or "treegrid")
            return false;
        if (role is "presentation" or "none")
            return true;
        if (RowCount <= 1 || ColumnCount <= 1)
            return true;
        return !Cells.Any(c => c.IsHeader) && string.IsNullOrWhiteSpace(Table.Name);
    }

    private static string Role(VBufferNode node) => node.AriaRole?.Trim().ToLowerInvariant() ?? string.Empty;
}
