using Microsoft.Extensions.Logging;
using Vox.Core.Navigation;

namespace Vox.Core.AppModules.Excel;

/// <summary>
/// The title row and title column the user set for a sheet (JAWS's titles, NVDA's column and row
/// headers), per workbook and sheet, saved like web tables' headers in their own file. Rows and
/// columns are 1-based, as in Excel. Thread-safe: titles are looked up on the Office thread and
/// set on the pipeline thread. Without a file (secure mode) they last the session.
/// </summary>
public sealed class ExcelTitleStore
{
    private readonly TableHeaderStore _store;
    private readonly object _lock = new();

    public ExcelTitleStore(string? path = null, ILogger<TableHeaderStore>? logger = null)
    {
        _store = new TableHeaderStore(path, logger);
    }

    /// <summary>The default file, in the user's Vox folder.</summary>
    public static string DefaultPath => Path.Combine(Lifecycle.VoxPaths.UserData, "excel-titles.json");

    /// <summary>The title row and column of a sheet, each null when not set.</summary>
    public (int? Row, int? Column) Get(string workbook, string sheet)
    {
        lock (_lock)
        {
            var headers = _store.Get(Key(workbook, sheet));
            return (headers.Row, headers.Column);
        }
    }

    /// <summary>Sets (or with null clears) the row holding a sheet's column titles.</summary>
    public void SetRow(string workbook, string sheet, int? row)
    {
        lock (_lock)
            _store.SetRow(Key(workbook, sheet), row);
    }

    /// <summary>Sets (or with null clears) the column holding a sheet's row titles.</summary>
    public void SetColumn(string workbook, string sheet, int? column)
    {
        lock (_lock)
            _store.SetColumn(Key(workbook, sheet), column);
    }

    private static string Key(string workbook, string sheet) => $"excel|{workbook}|{sheet}";
}
