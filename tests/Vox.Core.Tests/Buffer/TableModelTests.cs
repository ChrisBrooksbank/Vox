using Vox.Core.Buffer;
using Xunit;

namespace Vox.Core.Tests.Buffer;

public class TableModelTests
{
    private static int _nextId = 100;

    internal static MockElement Table(string name = "", string role = "table", params MockElement[] rows)
    {
        var table = new MockElement { RuntimeId = [_nextId++], ControlType = "Table", AriaRole = role, Name = name };
        foreach (var row in rows) table.AddChild(row);
        return table;
    }

    internal static MockElement Row(params MockElement[] cells)
    {
        var row = new MockElement { RuntimeId = [_nextId++], ControlType = "DataItem", AriaRole = "row" };
        foreach (var cell in cells) row.AddChild(cell);
        return row;
    }

    internal static MockElement Cell(string text, string role = "cell", int rowSpan = 1, int columnSpan = 1)
    {
        var cell = new MockElement
        {
            RuntimeId = [_nextId++], ControlType = "DataItem", AriaRole = role,
            RowSpan = rowSpan, ColumnSpan = columnSpan,
        };
        cell.AddChild(new MockElement { RuntimeId = [_nextId++], ControlType = "Text", Name = text });
        return cell;
    }

    internal static MockElement Header(string text, int columnSpan = 1) => Cell(text, "columnheader", columnSpan: columnSpan);
    internal static MockElement RowHeader(string text, int rowSpan = 1) => Cell(text, "rowheader", rowSpan: rowSpan);

    internal static (VBufferDocument Doc, TableModel Model) Build(MockElement table)
    {
        var root = new MockElement { RuntimeId = [_nextId++], ControlType = "Document", Name = "Page" };
        root.AddChild(table);
        var doc = new VBufferBuilder().Build(root);
        return (doc, Assert.Single(doc.TableModels));
    }

    private static string Text(TableCell? cell) => cell is null ? "" : cell.Node.Children[0].Name;

    [Fact]
    public void Build_PlacesCellsByRowAndColumn()
    {
        var (_, model) = Build(Table("Prices", "table",
            Row(Header("Item"), Header("Price")),
            Row(Cell("Tea"), Cell("2")),
            Row(Cell("Cake"), Cell("3"))));

        Assert.Equal(3, model.RowCount);
        Assert.Equal(2, model.ColumnCount);
        Assert.Equal("Cake", Text(model.CellAt(2, 0)));
        Assert.Equal("2", Text(model.CellAt(1, 1)));
        Assert.Null(model.CellAt(3, 0));
        Assert.Null(model.CellAt(0, 2));
    }

    [Fact]
    public void Build_LaysOutRowAndColumnSpans()
    {
        // | A (2 rows) | B  C (2 columns) |
        // |            | D  | E          |
        var (_, model) = Build(Table("T", "table",
            Row(Cell("A", rowSpan: 2), Cell("BC", columnSpan: 2)),
            Row(Cell("D"), Cell("E"))));

        Assert.Equal(2, model.RowCount);
        Assert.Equal(3, model.ColumnCount);
        Assert.Equal("A", Text(model.CellAt(1, 0)));
        Assert.Equal("BC", Text(model.CellAt(0, 2)));
        Assert.Equal("D", Text(model.CellAt(1, 1)));
        Assert.Equal("E", Text(model.CellAt(1, 2)));
        var d = model.Cells.Single(c => Text(c) == "D");
        Assert.Equal((1, 1), (d.Row, d.Column));
    }

    [Fact]
    public void Build_ClipsRowSpanPastTheLastRow()
    {
        var (_, model) = Build(Table("T", "table",
            Row(Cell("A", rowSpan: 5), Cell("B"))));

        Assert.Equal(1, model.RowCount);
        Assert.Equal(1, model.Cells[0].RowSpan);
    }

    [Fact]
    public void Build_FindsRowsInsideRowGroups()
    {
        var head = new MockElement { RuntimeId = [_nextId++], ControlType = "Group", AriaRole = "rowgroup" };
        head.AddChild(Row(Header("Name"), Header("Age")));
        var body = new MockElement { RuntimeId = [_nextId++], ControlType = "Group", AriaRole = "rowgroup" };
        body.AddChild(Row(Cell("Ann"), Cell("30")));
        var (_, model) = Build(Table("People", "table", head, body));

        Assert.Equal(2, model.RowCount);
        Assert.Equal("30", Text(model.CellAt(1, 1)));
    }

    [Fact]
    public void Headers_ColumnAndRowHeadersOfACell()
    {
        var (_, model) = Build(Table("Scores", "table",
            Row(Cell(""), Header("Mon"), Header("Tue")),
            Row(RowHeader("Ann"), Cell("1"), Cell("2")),
            Row(RowHeader("Bob"), Cell("3"), Cell("4"))));

        var cell = model.CellAt(2, 2)!;
        Assert.Equal(["Tue"], model.ColumnHeadersFor(cell).Select(Text));
        Assert.Equal(["Bob"], model.RowHeadersFor(cell).Select(Text));
        Assert.Empty(model.ColumnHeadersFor(model.CellAt(0, 1)!));
    }

    [Fact]
    public void Headers_SpanningHeaderAppliesToEveryColumnItCovers()
    {
        var (_, model) = Build(Table("T", "table",
            Row(Header("Week", columnSpan: 2)),
            Row(Header("Mon"), Header("Tue")),
            Row(Cell("1"), Cell("2"))));

        Assert.Equal(["Week", "Tue"], model.ColumnHeadersFor(model.CellAt(2, 1)!).Select(Text));
    }

    [Fact]
    public void Build_NestedTableCellsBelongToTheNestedTable()
    {
        var inner = Table("Inner", "table", Row(Header("X"), Header("Y")), Row(Cell("x1"), Cell("y1")));
        var outerCell = new MockElement { RuntimeId = [_nextId++], ControlType = "DataItem", AriaRole = "cell" };
        outerCell.AddChild(inner);
        var outer = Table("Outer", "table", Row(Header("Left"), Header("Right")), Row(outerCell, Cell("r")));
        var root = new MockElement { RuntimeId = [_nextId++], ControlType = "Document" };
        root.AddChild(outer);
        var doc = new VBufferBuilder().Build(root);

        Assert.Equal(2, doc.TableModels.Count);
        var outerModel = doc.TableModels[0];
        Assert.Equal(4, outerModel.Cells.Count);
        Assert.Equal(2, outerModel.RowCount);

        var y1 = doc.AllNodes.First(n => n.Name == "y1");
        var found = doc.FindTableCell(y1);
        Assert.NotNull(found);
        Assert.Same(doc.TableModels[1], found.Value.Table);
        Assert.Equal((1, 1), (found.Value.Cell.Row, found.Value.Cell.Column));

        var r = doc.AllNodes.First(n => n.Name == "r");
        Assert.Same(outerModel, doc.FindTableCell(r)!.Value.Table);
    }

    [Fact]
    public void FindTableCell_NullOutsideTables()
    {
        var (doc, _) = Build(Table("T", "table", Row(Header("A"), Header("B")), Row(Cell("1"), Cell("2"))));
        Assert.Null(doc.FindTableCell(doc.Root));
    }

    [Fact]
    public void IsLayoutTable_DataTableWithHeadersIsNot()
    {
        var (_, model) = Build(Table("", "table", Row(Header("A"), Header("B")), Row(Cell("1"), Cell("2"))));
        Assert.False(model.IsLayoutTable);
    }

    [Fact]
    public void IsLayoutTable_NamedTableWithoutHeadersIsNot()
    {
        var (_, model) = Build(Table("Results", "table", Row(Cell("A"), Cell("B")), Row(Cell("1"), Cell("2"))));
        Assert.False(model.IsLayoutTable);
    }

    [Fact]
    public void IsLayoutTable_UnnamedTableWithoutHeaders()
    {
        var (_, model) = Build(Table("", "table", Row(Cell("A"), Cell("B")), Row(Cell("1"), Cell("2"))));
        Assert.True(model.IsLayoutTable);
    }

    [Fact]
    public void IsLayoutTable_SingleRowOrColumn()
    {
        Assert.True(Build(Table("T", "table", Row(Header("A"), Header("B")))).Model.IsLayoutTable);
        Assert.True(Build(Table("T", "table", Row(Header("A")), Row(Cell("1")))).Model.IsLayoutTable);
    }

    [Fact]
    public void IsLayoutTable_GridsNeverAre()
    {
        var (_, model) = Build(Table("", "grid", Row(Cell("A", "gridcell")), Row(Cell("1", "gridcell"))));
        Assert.False(model.IsLayoutTable);
    }

    [Fact]
    public void Build_TableWithoutCellsHasNoModel()
    {
        var root = new MockElement { RuntimeId = [_nextId++], ControlType = "Document" };
        root.AddChild(new MockElement { RuntimeId = [_nextId++], ControlType = "Table", AriaRole = "table", Name = "Empty" });
        var doc = new VBufferBuilder().Build(root);

        Assert.Single(doc.Tables);
        Assert.Empty(doc.TableModels);
        Assert.Null(doc.GetTableModel(doc.Tables[0]));
    }

    [Fact]
    public void IncrementalUpdate_KeepsSpans()
    {
        var node = new VBufferNode { RowSpan = 2, ColumnSpan = 3 };
        var copy = node.CloneDetached(5, (0, 0));
        Assert.Equal((2, 3), (copy.RowSpan, copy.ColumnSpan));
    }
}
