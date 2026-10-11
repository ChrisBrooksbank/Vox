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

        parts.Add(string.IsNullOrWhiteSpace(cell.Text) ? "blank" : cell.Text.Trim());
        parts.Add(cell.MergedArea is { } merged ? $"{cell.Address}, merged {ExcelAddress.Speakable(merged)}" : cell.Address);

        if (options.ReportFormulas && cell.HasFormula)
            parts.Add("has formula");
        if (options.ReportCommentsAndNotes)
        {
            if (cell.Comment is not null)
                parts.Add("has comment");
            if (cell.Note is not null)
                parts.Add("has note");
        }
        if (cell.HasFilter)
            parts.Add("filter on");
        if (!string.IsNullOrWhiteSpace(cell.ValidationMessage))
        {
            parts.Add(string.IsNullOrWhiteSpace(cell.ValidationTitle)
                ? cell.ValidationMessage.Trim()
                : $"{cell.ValidationTitle.Trim()}: {cell.ValidationMessage.Trim()}");
        }
        return Join(parts);
    }

    /// <summary>"A1 to C5 selected, 3 rows, 5 columns, 15 cells".</summary>
    public static string Selection(ExcelSelection selection)
    {
        var size = selection.Rows > 1 && selection.Columns > 1
            ? $"{Count(selection.Rows, "row")}, {Count(selection.Columns, "column")}, "
            : string.Empty;
        return $"{ExcelAddress.Speakable(selection.Address)} selected, {size}{Count(selection.Cells, "cell")}";
    }

    /// <summary>The formula command: the formula, or the value when the cell has none.</summary>
    public static string Formula(ExcelCell cell) =>
        cell.HasFormula && !string.IsNullOrWhiteSpace(cell.Formula)
            ? $"{cell.Address} formula {cell.Formula.Trim()}"
            : $"{cell.Address} has no formula, {(string.IsNullOrWhiteSpace(cell.Text) ? "blank" : cell.Text.Trim())}";

    /// <summary>The comment command: the comment and the note, or that there are none.</summary>
    public static string CommentAndNote(ExcelCell cell)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(cell.Comment))
            parts.Add($"Comment: {cell.Comment.Trim()}");
        if (!string.IsNullOrWhiteSpace(cell.Note))
            parts.Add($"Note: {cell.Note.Trim()}");
        return parts.Count == 0 ? $"{cell.Address} has no comment or note" : Join(parts);
    }

    /// <summary>The chart command: "Chart Sales, column chart, 2 series: 2023, 2024".</summary>
    public static string Chart(ExcelChart? chart)
    {
        if (chart is null)
            return "No chart on this sheet";
        var title = string.IsNullOrWhiteSpace(chart.Title) ? "Untitled chart" : $"Chart {chart.Title.Trim()}";
        var series = chart.Series.Count == 0
            ? "no series"
            : $"{Count(chart.Series.Count, "series", "series")}: {string.Join(", ", chart.Series)}";
        return $"{title}, {chart.Type}, {series}";
    }

    /// <summary>A name for an Excel XlChartType value.</summary>
    public static string ChartTypeName(int xlChartType) => xlChartType switch
    {
        51 or 52 or 53 or 54 or 55 or 56 => "column chart",
        57 or 58 or 59 or 60 or 61 or 62 => "bar chart",
        4 or 63 or 64 or 65 or 66 or 67 or -4101 => "line chart",
        5 or -4102 or 68 or 69 or 70 or 71 => "pie chart",
        -4120 or 80 => "doughnut chart",
        1 or 76 or 77 or 78 or 79 or -4098 => "area chart",
        -4169 or 72 or 73 or 74 or 75 => "scatter chart",
        15 or 87 => "bubble chart",
        -4151 or 81 or 82 => "radar chart",
        _ => "chart",
    };

    private static string Count(long n, string singular, string? plural = null) =>
        n == 1 ? $"1 {singular}" : $"{n} {plural ?? singular + "s"}";

    private static string Join(List<string> parts) => string.Join(", ", parts);
}
