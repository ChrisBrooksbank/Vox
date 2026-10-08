using Vox.Core.Buffer;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Xunit;
using static Vox.Core.Tests.Buffer.TableModelTests;

namespace Vox.Core.Tests.Navigation;

public class TableNavigatorTests
{
    private readonly TableNavigator _navigator = new();

    //            | Mon | Tue
    //   Ann      | 1   | 2
    //   Bob      | 3   | 4
    private static (VBufferDocument Doc, TableModel Model) Scores() => Build(Table("Scores", "table",
        Row(Cell(""), Header("Mon"), Header("Tue")),
        Row(RowHeader("Ann"), Cell("1"), Cell("2")),
        Row(RowHeader("Bob"), Cell("3"), Cell("4"))));

    private static VBufferNode TextNode(VBufferDocument doc, string text) =>
        doc.AllNodes.First(n => n.Name == text && n.ControlType == "Text");

    [Fact]
    public void Move_OutsideATable_SaysSo()
    {
        var (doc, _) = Scores();
        var result = _navigator.Move(doc, doc.Root, NavigationCommand.TableNextColumn);
        Assert.True(result.NotInTable);
        Assert.Null(result.Cell);
    }

    [Fact]
    public void Move_EnteringATable_SaysItsDimensionsAndPosition()
    {
        var (doc, model) = Scores();
        var result = _navigator.Move(doc, TextNode(doc, "1"), NavigationCommand.TableNextColumn);

        Assert.Same(model.CellAt(1, 2), result.Cell);
        Assert.Equal("table with 3 rows and 3 columns, Ann, row 2, Tue, column 3, 2", result.Text);
    }

    [Fact]
    public void Move_WithinARow_SaysTheNewColumnAndItsHeader()
    {
        var (doc, model) = Scores();
        _navigator.Move(doc, TextNode(doc, "Ann"), NavigationCommand.TableFirstCell);

        var result = _navigator.Move(doc, TextNode(doc, "1"), NavigationCommand.TableNextColumn);

        Assert.Same(model.CellAt(1, 2), result.Cell);
        Assert.Equal("Tue, column 3, 2", result.Text);
    }

    [Fact]
    public void Move_DownAColumn_SaysTheNewRowAndItsHeader()
    {
        var (doc, model) = Scores();
        _navigator.Move(doc, TextNode(doc, "1"), NavigationCommand.TableFirstCell);
        _navigator.Move(doc, model.CellAt(0, 0)!.Node, NavigationCommand.TableNextRow); // Ann

        var result = _navigator.Move(doc, model.CellAt(1, 0)!.Node, NavigationCommand.TableNextColumn);
        Assert.Equal("Mon, column 2, 1", result.Text);

        result = _navigator.Move(doc, result.Cell!.Node, NavigationCommand.TableNextRow);
        Assert.Same(model.CellAt(2, 1), result.Cell);
        Assert.Equal("Bob, row 3, 3", result.Text);
    }

    [Fact]
    public void Move_EmptyCellIsBlank()
    {
        var (doc, model) = Scores();
        _navigator.Move(doc, TextNode(doc, "Ann"), NavigationCommand.TableFirstCell);
        var result = _navigator.Move(doc, model.CellAt(1, 0)!.Node, NavigationCommand.TablePrevRow);

        Assert.Same(model.CellAt(0, 0), result.Cell);
        Assert.Equal("row 1, blank", result.Text);
    }

    [Theory]
    [InlineData(NavigationCommand.TablePrevColumn)]
    [InlineData(NavigationCommand.TablePrevRow)]
    public void Move_PastTheFirstCell_IsTheEdge(NavigationCommand command)
    {
        var (doc, model) = Scores();
        var result = _navigator.Move(doc, model.CellAt(0, 0)!.Node, command);
        Assert.True(result.AtEdge);
        Assert.Null(result.Cell);
    }

    [Theory]
    [InlineData(NavigationCommand.TableNextColumn)]
    [InlineData(NavigationCommand.TableNextRow)]
    public void Move_PastTheLastCell_IsTheEdge(NavigationCommand command)
    {
        var (doc, model) = Scores();
        Assert.True(_navigator.Move(doc, model.CellAt(2, 2)!.Node, command).AtEdge);
    }

    [Fact]
    public void FirstAndLastCell()
    {
        var (doc, model) = Scores();
        Assert.Same(model.CellAt(2, 2), _navigator.Move(doc, TextNode(doc, "1"), NavigationCommand.TableLastCell).Cell);
        Assert.Same(model.CellAt(0, 0), _navigator.Move(doc, TextNode(doc, "4"), NavigationCommand.TableFirstCell).Cell);
    }

    [Fact]
    public void Move_ThroughAColumnSpanningCell_KeepsTheColumn()
    {
        // | A | B  | C |
        // |   wide     |
        // | D | E  | F |
        var (doc, model) = Build(Table("T", "table",
            Row(Header("A"), Header("B"), Header("C")),
            Row(Cell("wide", columnSpan: 3)),
            Row(Cell("D"), Cell("E"), Cell("F"))));
        _navigator.Move(doc, TextNode(doc, "B"), NavigationCommand.TableFirstCell);
        var b = _navigator.Move(doc, model.CellAt(0, 0)!.Node, NavigationCommand.TableNextColumn);

        var wide = _navigator.Move(doc, b.Cell!.Node, NavigationCommand.TableNextRow);
        Assert.Equal("wide", Text(wide.Cell));

        var e = _navigator.Move(doc, wide.Cell!.Node, NavigationCommand.TableNextRow);
        Assert.Equal("E", Text(e.Cell));
        Assert.Equal("row 3, E", e.Text);
    }

    [Fact]
    public void Move_RightFromASpanningCell_SkipsTheColumnsItCovers()
    {
        var (doc, model) = Build(Table("T", "table",
            Row(Header("AB", columnSpan: 2), Header("C")),
            Row(Cell("1"), Cell("2"), Cell("3"))));

        var result = _navigator.Move(doc, model.CellAt(0, 0)!.Node, NavigationCommand.TableNextColumn);
        Assert.Equal("C", Text(result.Cell));
    }

    [Fact]
    public void Move_ShortRow_IsTheEdge()
    {
        var (doc, model) = Build(Table("T", "table",
            Row(Header("A"), Header("B")),
            Row(Cell("1"))));
        Assert.True(_navigator.Move(doc, model.CellAt(1, 0)!.Node, NavigationCommand.TableNextColumn).AtEdge);
    }

    [Fact]
    public void Reset_AnnouncesTheTableAgain()
    {
        var (doc, model) = Scores();
        _navigator.Move(doc, TextNode(doc, "1"), NavigationCommand.TableFirstCell);
        _navigator.Reset();
        var result = _navigator.Move(doc, model.CellAt(0, 0)!.Node, NavigationCommand.TableNextColumn);
        Assert.StartsWith("table with 3 rows and 3 columns", result.Text);
    }

    [Fact]
    public void Dimensions_Singular()
    {
        var (_, model) = Build(Table("T", "table", Row(Header("A"), Header("B"))));
        Assert.Equal("table with 1 row and 2 columns", TableNavigator.Dimensions(model));
    }

    private static string Text(TableCell? cell) => cell is null ? "" : cell.Node.Children[0].Name;
}
