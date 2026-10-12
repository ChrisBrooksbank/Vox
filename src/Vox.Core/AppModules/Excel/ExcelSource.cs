using Vox.Core.AppModules.Office;
using Vox.Core.Localization;

namespace Vox.Core.AppModules.Excel;

/// <summary>Reads the active cell and charts of the Excel instance that owns a window.</summary>
public interface IExcelSource
{
    /// <summary>The active cell (with the selection when more than one cell is selected), or null when Excel can't say (busy editing a cell, closed).</summary>
    Task<ExcelCell?> ActiveCellAsync(IntPtr window);

    /// <summary>The selected chart, else the active sheet's first chart; null when there is none.</summary>
    Task<ExcelChart?> ChartAsync(IntPtr window);
}

/// <summary>
/// <see cref="IExcelSource"/> through Excel's object model (late bound, on the Office thread).
/// Every optional part (comments, validation, filters, merged cells) is read on its own, so one
/// that throws (Excel throws for "no validation") leaves the rest.
/// </summary>
public sealed class ExcelObjectModelSource : IExcelSource
{
    private readonly IOfficeObjectModel _objectModel;
    private readonly ExcelTitleStore _titles;

    public ExcelObjectModelSource(IOfficeObjectModel objectModel, ExcelTitleStore titles)
    {
        _objectModel = objectModel;
        _titles = titles;
    }

    public Task<ExcelCell?> ActiveCellAsync(IntPtr window) =>
        _objectModel.RunAsync<ExcelCell>(OfficeApp.Excel, window, app => ReadCell(app, _titles));

    public Task<ExcelChart?> ChartAsync(IntPtr window) =>
        _objectModel.RunAsync<ExcelChart>(OfficeApp.Excel, window, app => ReadChart(app));

    /// <summary>The active cell of <paramref name="app"/> (an Excel Application object, or a fake of one).</summary>
    public static ExcelCell? ReadCell(dynamic app, ExcelTitleStore titles)
    {
        dynamic cell = app.ActiveCell;
        if (cell is null)
            return null;
        dynamic sheet = app.ActiveSheet;
        string workbook = (string)app.ActiveWorkbook.FullName;
        string sheetName = (string)sheet.Name;
        int row = (int)cell.Row;
        int column = (int)cell.Column;

        var (titleRow, titleColumn) = titles.Get(workbook, sheetName);
        string? columnTitle = titleRow is { } tr && tr != row ? Try(() => (string)sheet.Cells(tr, column).Text) : null;
        string? rowTitle = titleColumn is { } tc && tc != column ? Try(() => (string)sheet.Cells(row, tc).Text) : null;

        bool hasFormula = Try(() => (bool)cell.HasFormula);
        return new ExcelCell(
            workbook, sheetName, row, column,
            Text: Try(() => (string)cell.Text) ?? string.Empty,
            HasFormula: hasFormula,
            Formula: hasFormula ? Try(() => (string)cell.Formula) : null,
            Comment: Try(() => cell.CommentThreaded is null ? null : (string)cell.CommentThreaded.Text()),
            Note: Try(() => cell.Comment is null ? null : (string)cell.Comment.Text()),
            ValidationTitle: Try(() => NullIfEmpty((string)cell.Validation.InputTitle)),
            ValidationMessage: Try(() => (bool)cell.Validation.ShowInput ? NullIfEmpty((string)cell.Validation.InputMessage) : null),
            HasFilter: Try<bool>(() => HasFilter(sheet, row, column)),
            MergedArea: Try(() => (bool)cell.MergeCells ? (string)cell.MergeArea.Address(false, false) : null),
            ColumnTitle: NullIfEmpty(columnTitle),
            RowTitle: NullIfEmpty(rowTitle),
            Selection: Try<ExcelSelection>(() => ReadSelection(app.Selection)));
    }

    private static ExcelSelection? ReadSelection(dynamic selection)
    {
        long cells = (long)selection.CountLarge;
        if (cells <= 1)
            return null;
        return new ExcelSelection((string)selection.Address(false, false), (int)selection.Rows.Count, (int)selection.Columns.Count, cells);
    }

    /// <summary>Whether the cell heads a column of the sheet's AutoFilter whose filter is on.</summary>
    private static bool HasFilter(dynamic sheet, int row, int column)
    {
        if (!(bool)sheet.AutoFilterMode)
            return false;
        dynamic range = sheet.AutoFilter.Range;
        int first = (int)range.Column;
        if ((int)range.Row != row || column < first || column >= first + (int)range.Columns.Count)
            return false;
        return (bool)sheet.AutoFilter.Filters(column - first + 1).On;
    }

    /// <summary>The selected chart, else the first on the active sheet.</summary>
    public static ExcelChart? ReadChart(dynamic app)
    {
        dynamic? chart = Try<object>(() => app.ActiveChart);
        if (chart is null)
        {
            dynamic objects = app.ActiveSheet.ChartObjects();
            if ((int)objects.Count == 0)
                return null;
            chart = objects.Item(1).Chart;
        }
        string? title = Try(() => (bool)chart.HasTitle ? (string)chart.ChartTitle.Text : null);
        var series = new List<string>();
        dynamic collection = chart.SeriesCollection();
        int count = (int)collection.Count;
        for (int i = 1; i <= count; i++)
            series.Add(Try(() => (string)collection.Item(i).Name) ?? VoxStrings.Format("Excel.SeriesNumber", i));
        return new ExcelChart(title, ExcelAnnouncer.ChartTypeName((int)chart.ChartType), series);
    }

    private static T? Try<T>(Func<T?> read)
    {
        try { return read(); }
        catch { return default; }
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
