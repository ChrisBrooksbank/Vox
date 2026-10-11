using Vox.Core.AppModules.Excel;
using Xunit;

namespace Vox.Core.Tests.AppModules.Excel;

public class ExcelAnnouncerTests
{
    private static readonly ExcelAnnouncementOptions All = new();

    private static ExcelCell Cell(int row, int column, string text = "42", string sheet = "Budget") =>
        new("C:\\Budget.xlsx", sheet, row, column, text);

    [Theory]
    [InlineData(1, "A")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(52, "AZ")]
    [InlineData(703, "AAA")]
    [InlineData(16384, "XFD")]
    public void ColumnLetters_RoundTrip(int column, string letters)
    {
        Assert.Equal(letters, ExcelAddress.ColumnLetters(column));
        Assert.Equal(column, ExcelAddress.ColumnNumber(letters));
        Assert.Equal(column, ExcelAddress.ColumnNumber(letters.ToLowerInvariant()));
    }

    [Fact]
    public void ColumnNumber_NotLetters_IsZero() => Assert.Equal(0, ExcelAddress.ColumnNumber("A1"));

    [Fact]
    public void Speakable_DropsAbsoluteMarks_AndSaysRanges() =>
        Assert.Equal("A1 to C5, E1", ExcelAddress.Speakable("$A$1:$C$5,$E$1"));

    [Fact]
    public void Cell_SaysValueThenAddress()
    {
        Assert.Equal("42, B5", ExcelAnnouncer.Cell(Cell(5, 2), Cell(4, 2), All));
    }

    [Fact]
    public void Cell_Empty_IsBlank() =>
        Assert.Equal("blank, B5", ExcelAnnouncer.Cell(Cell(5, 2, " "), Cell(4, 2), All));

    [Fact]
    public void Cell_SaysTheSheet_WhenItChanged_ButNotComingFromOutsideExcel()
    {
        Assert.Equal("Totals, 42, A1", ExcelAnnouncer.Cell(Cell(1, 1, sheet: "Totals"), Cell(1, 1), All));
        Assert.Equal("42, A1", ExcelAnnouncer.Cell(Cell(1, 1, sheet: "Totals"), null, All));
    }

    [Fact]
    public void Titles_AreSaidOnlyWhenTheirRowOrColumnChanges()
    {
        var cell = Cell(5, 3) with { ColumnTitle = "March", RowTitle = "Rent" };

        // Down the column: the row title only
        Assert.Equal("Rent, 42, C5", ExcelAnnouncer.Cell(cell, Cell(4, 3), All));
        // Along the row: the column title only
        Assert.Equal("March, 42, C5", ExcelAnnouncer.Cell(cell, Cell(5, 2), All));
        // Arriving: both
        Assert.Equal("March, Rent, 42, C5", ExcelAnnouncer.Cell(cell, null, All));
    }

    [Fact]
    public void Extras_FollowTheAddress_AsTheSettingsSay()
    {
        var cell = Cell(2, 2) with
        {
            HasFormula = true, Formula = "=A2*2", Comment = "Check this", Note = "Old", HasFilter = true,
            ValidationTitle = "Amount", ValidationMessage = "Enter pounds", MergedArea = "$B$2:$C$2",
        };

        Assert.Equal("42, B2, merged B2 to C2, has formula, has comment, has note, filter on, Amount: Enter pounds",
            ExcelAnnouncer.Cell(cell, Cell(1, 2), All));
        Assert.Equal("42, B2, merged B2 to C2, filter on, Amount: Enter pounds",
            ExcelAnnouncer.Cell(cell, Cell(1, 2), new ExcelAnnouncementOptions(ReportFormulas: false, ReportCommentsAndNotes: false)));
    }

    [Fact]
    public void Selection_IsSaidInsteadOfTheCell()
    {
        var cell = Cell(1, 1) with { Selection = new ExcelSelection("A1:C5", 5, 3, 15) };

        Assert.Equal("A1 to C5 selected, 5 rows, 3 columns, 15 cells", ExcelAnnouncer.Cell(cell, Cell(1, 1), All));
        Assert.Equal("A1 to A3 selected, 3 cells", ExcelAnnouncer.Selection(new ExcelSelection("A1:A3", 3, 1, 3)));
    }

    [Fact]
    public void Formula_SaysTheFormula_OrThatThereIsNone()
    {
        Assert.Equal("B2 formula =SUM(A1:A9)", ExcelAnnouncer.Formula(Cell(2, 2) with { HasFormula = true, Formula = "=SUM(A1:A9)" }));
        Assert.Equal("B2 has no formula, 42", ExcelAnnouncer.Formula(Cell(2, 2)));
    }

    [Fact]
    public void CommentAndNote()
    {
        Assert.Equal("Comment: Why?, Note: Because", ExcelAnnouncer.CommentAndNote(Cell(1, 1) with { Comment = "Why?", Note = "Because" }));
        Assert.Equal("A1 has no comment or note", ExcelAnnouncer.CommentAndNote(Cell(1, 1)));
    }

    [Fact]
    public void Chart()
    {
        Assert.Equal("Chart Sales, column chart, 2 series: 2023, 2024",
            ExcelAnnouncer.Chart(new ExcelChart("Sales", ExcelAnnouncer.ChartTypeName(51), ["2023", "2024"])));
        Assert.Equal("Untitled chart, pie chart, 1 series: Share",
            ExcelAnnouncer.Chart(new ExcelChart(null, ExcelAnnouncer.ChartTypeName(5), ["Share"])));
        Assert.Equal("No chart on this sheet", ExcelAnnouncer.Chart(null));
    }
}
