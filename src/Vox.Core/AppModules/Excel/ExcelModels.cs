namespace Vox.Core.AppModules.Excel;

/// <summary>
/// What Vox reads about the active cell from Excel's object model. <see cref="Row"/> and
/// <see cref="Column"/> are 1-based, as in Excel. Text is the cell as shown (formatted).
/// </summary>
public sealed record ExcelCell(
    string Workbook,
    string Sheet,
    int Row,
    int Column,
    string Text,
    bool HasFormula = false,
    string? Formula = null,
    string? Comment = null,
    string? Note = null,
    string? ValidationTitle = null,
    string? ValidationMessage = null,
    bool HasFilter = false,
    string? MergedArea = null,
    string? ColumnTitle = null,
    string? RowTitle = null,
    ExcelSelection? Selection = null)
{
    public string Address => ExcelAddress.Format(Row, Column);
}

/// <summary>A selection of more than one cell: its address ("A1:C5") and size.</summary>
public sealed record ExcelSelection(string Address, int Rows, int Columns, long Cells);

/// <summary>A chart: its title, type and the names of its series.</summary>
public sealed record ExcelChart(string? Title, string Type, IReadOnlyList<string> Series);
