using Vox.Core.Localization;

namespace Vox.Core.AppModules.Excel;

/// <summary>What Excel cell announcements include (from the settings).</summary>
public sealed record ExcelAnnouncementOptions(bool ReportFormulas = true, bool ReportCommentsAndNotes = true);

/// <summary>
/// Composes what is said for Excel cells, as JAWS does: the sheet when it changed, the column
/// title when the column changed and the row title when the row changed (titles the user set,
/// <see cref="ExcelTitleStore"/>), then the value and address, and what else the cell has.
/// </summary>
public static class ExcelAnnouncer
{
    /// <summary>The announcement for moving to <paramref name="cell"/> from <paramref name="previous"/> (null: from outside the sheet).</summary>
    public static string Cell(ExcelCell cell, ExcelCell? previous, ExcelAnnouncementOptions options)
    {
        var parts = new List<string>();
        bool sameSheet = previous is not null && previous.Workbook == cell.Workbook && previous.Sheet == cell.Sheet;
        if (!sameSheet && previous is not null)
            parts.Add(cell.Sheet);

        if (cell.Selection is { } selection)
        {
            parts.Add(Selection(selection));
            return Join(parts);
        }

        // Titles only when they change, so moving along a row isn't drowned in its row title
        if (!string.IsNullOrWhiteSpace(cell.ColumnTitle) && (!sameSheet || previous!.Column != cell.Column))
            parts.Add(cell.ColumnTitle.Trim());
        if (!string.IsNullOrWhiteSpace(cell.RowTitle) && (!sameSheet || previous!.Row != cell.Row))
            parts.Add(cell.RowTitle.Trim());

        parts.Add(string.IsNullOrWhiteSpace(cell.Text) ? VoxStrings.Get("AppModule.Blank") : cell.Text.Trim());
        parts.Add(cell.MergedArea is { } merged ? VoxStrings.Format("Excel.Merged", cell.Address, ExcelAddress.Speakable(merged)) : cell.Address);

        if (options.ReportFormulas && cell.HasFormula)
            parts.Add(VoxStrings.Get("Excel.HasFormula"));
        if (options.ReportCommentsAndNotes)
        {
            if (cell.Comment is not null)
                parts.Add(VoxStrings.Get("Excel.HasComment"));
            if (cell.Note is not null)
                parts.Add(VoxStrings.Get("Excel.HasNote"));
        }
        if (cell.HasFilter)
            parts.Add(VoxStrings.Get("Excel.FilterOn"));
        if (!string.IsNullOrWhiteSpace(cell.ValidationMessage))
        {
            parts.Add(string.IsNullOrWhiteSpace(cell.ValidationTitle)
                ? cell.ValidationMessage.Trim()
                : VoxStrings.Format("Excel.Validation", cell.ValidationTitle.Trim(), cell.ValidationMessage.Trim()));
        }
        return Join(parts);
    }

    /// <summary>"A1 to C5 selected, 3 rows, 5 columns, 15 cells".</summary>
    public static string Selection(ExcelSelection selection)
    {
        var address = ExcelAddress.Speakable(selection.Address);
        var cells = selection.Cells == 1 ? VoxStrings.Get("Excel.OneCell") : VoxStrings.Format("Excel.Cells", selection.Cells);
        return selection.Rows > 1 && selection.Columns > 1
            ? VoxStrings.Format("Excel.SelectionWithSize", address, Rows(selection.Rows), Columns(selection.Columns), cells)
            : VoxStrings.Format("Excel.Selection", address, cells);
    }

    /// <summary>The formula command: the formula, or the value when the cell has none.</summary>
    public static string Formula(ExcelCell cell) =>
        cell.HasFormula && !string.IsNullOrWhiteSpace(cell.Formula)
            ? VoxStrings.Format("Excel.Formula", cell.Address, cell.Formula.Trim())
            : VoxStrings.Format("Excel.NoFormula", cell.Address, string.IsNullOrWhiteSpace(cell.Text) ? VoxStrings.Get("AppModule.Blank") : cell.Text.Trim());

    /// <summary>The comment command: the comment and the note, or that there are none.</summary>
    public static string CommentAndNote(ExcelCell cell)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(cell.Comment))
            parts.Add(VoxStrings.Format("Excel.Comment", cell.Comment.Trim()));
        if (!string.IsNullOrWhiteSpace(cell.Note))
            parts.Add(VoxStrings.Format("Excel.Note", cell.Note.Trim()));
        return parts.Count == 0 ? VoxStrings.Format("Excel.NoCommentOrNote", cell.Address) : Join(parts);
    }

    /// <summary>The chart command: "Chart Sales, column chart, 2 series: 2023, 2024".</summary>
    public static string Chart(ExcelChart? chart)
    {
        if (chart is null)
            return VoxStrings.Get("Excel.NoChart");
        var title = string.IsNullOrWhiteSpace(chart.Title) ? VoxStrings.Get("Excel.UntitledChart") : VoxStrings.Format("Excel.ChartTitled", chart.Title.Trim());
        var series = chart.Series.Count == 0
            ? VoxStrings.Get("Excel.NoSeries")
            : chart.Series.Count == 1
                ? VoxStrings.Format("Excel.OneSeries", chart.Series[0])
                : VoxStrings.Format("Excel.Series", chart.Series.Count, string.Join(", ", chart.Series));
        return VoxStrings.Format("Excel.ChartSummary", title, chart.Type, series);
    }

    /// <summary>A name for an Excel XlChartType value.</summary>
    public static string ChartTypeName(int xlChartType) => xlChartType switch
    {
        51 or 52 or 53 or 54 or 55 or 56 => VoxStrings.Get("Excel.ChartType.Column"),
        57 or 58 or 59 or 60 or 61 or 62 => VoxStrings.Get("Excel.ChartType.Bar"),
        4 or 63 or 64 or 65 or 66 or 67 or -4101 => VoxStrings.Get("Excel.ChartType.Line"),
        5 or -4102 or 68 or 69 or 70 or 71 => VoxStrings.Get("Excel.ChartType.Pie"),
        -4120 or 80 => VoxStrings.Get("Excel.ChartType.Doughnut"),
        1 or 76 or 77 or 78 or 79 or -4098 => VoxStrings.Get("Excel.ChartType.Area"),
        -4169 or 72 or 73 or 74 or 75 => VoxStrings.Get("Excel.ChartType.Scatter"),
        15 or 87 => VoxStrings.Get("Excel.ChartType.Bubble"),
        -4151 or 81 or 82 => VoxStrings.Get("Excel.ChartType.Radar"),
        _ => VoxStrings.Get("Excel.ChartType.Other"),
    };

    private static string Rows(long n) => n == 1 ? VoxStrings.Get("AppModule.OneRow") : VoxStrings.Format("AppModule.Rows", n);

    private static string Columns(long n) => n == 1 ? VoxStrings.Get("AppModule.OneColumn") : VoxStrings.Format("AppModule.Columns", n);

    private static string Join(List<string> parts) => string.Join(", ", parts);
}
