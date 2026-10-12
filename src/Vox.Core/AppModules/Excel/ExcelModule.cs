using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Localization;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.AppModules.Excel;

/// <summary>
/// Microsoft Excel. A cell taking focus is read from the object model (value, address, titles,
/// formula, comment, note, filter, validation message, the selection), so its UIA announcement is
/// replaced: the object model is asked off the pipeline thread and the answer is spoken back on it,
/// unless the user has moved on since. When Excel can't answer (a cell being edited, a dialog) Vox's
/// own announcement is said instead. Commands read the formula, the comment and note, a chart, and
/// set the sheet's title row and column.
/// </summary>
public sealed class ExcelModule : AppModule
{
    private readonly IExcelSource _source;
    private readonly ExcelTitleStore _titles;
    private readonly SpeechQueue _speech;
    private readonly IEventSink _events;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly ILogger _logger;

    // Pipeline thread only
    private long _sequence;
    // The sequence of a cell read not answered yet, or 0
    private long _pendingCellRead;
    private ExcelCell? _lastCell;
    private bool _onCell;

    public ExcelModule(IExcelSource source, ExcelTitleStore titles, SpeechQueue speech, IEventSink events,
        IOptionsMonitor<VoxSettings> settings, ILogger<ExcelModule>? logger = null)
    {
        _source = source;
        _titles = titles;
        _speech = speech;
        _events = events;
        _settings = settings;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public override string Id => "excel";
    public override string Name => "Microsoft Excel";
    public override IReadOnlyCollection<string> ProcessNames { get; } = ["EXCEL"];

    private IntPtr Window => Activation?.WindowHandle ?? IntPtr.Zero;

    public override void OnActivated(AppActivation activation)
    {
        base.OnActivated(activation);
        // Coming back to Excel: the sheet is said again with the first cell, and an answer from
        // before is stale
        _sequence++;
        _lastCell = null;
        _onCell = false;
    }

    public override string? FormatFocusAnnouncement(FocusChangedEvent focus, string announcement)
    {
        if (!IsCell(focus))
        {
            // A cell read still on its way would be said over this control
            _sequence++;
            _onCell = false;
            return announcement;
        }
        // Moving from cell to cell cuts the last one off; the first cell after another control or
        // window is queued after the window's title
        bool interrupt = _onCell;
        _onCell = true;
        ReadCell(announcement, interrupt, selectionOnly: false);
        return null;
    }

    public override void OnEvent(ScreenReaderEvent evt)
    {
        // Extending the selection (Shift+arrows) selects cells without moving the focus. Moving
        // to a cell selects it too: the cell read on its way already says the selection.
        if (evt is ElementSelectedEvent && _onCell && _pendingCellRead != _sequence)
            ReadCell(null, interrupt: true, selectionOnly: true);
    }

    /// <summary>A grid cell: Excel's cells are UIA data items.</summary>
    public static bool IsCell(FocusChangedEvent focus) =>
        string.Equals(focus.ControlType, "DataItem", StringComparison.OrdinalIgnoreCase);

    private void ReadCell(string? fallback, bool interrupt, bool selectionOnly)
    {
        long sequence = ++_sequence;
        if (!selectionOnly)
            _pendingCellRead = sequence;
        var window = Window;
        _ = Task.Run(async () =>
        {
            var cell = await _source.ActiveCellAsync(window).ConfigureAwait(false);
            _events.Post(new PipelineWorkEvent(DateTimeOffset.UtcNow, () => CellRead(sequence, cell, fallback, interrupt, selectionOnly)));
        });
    }

    private void CellRead(long sequence, ExcelCell? cell, string? fallback, bool interrupt, bool selectionOnly)
    {
        if (sequence == _pendingCellRead)
            _pendingCellRead = 0;
        // The user has moved on
        if (sequence != _sequence || !IsActive)
            return;
        string? text;
        if (cell is null)
            text = selectionOnly ? null : fallback;
        else if (selectionOnly)
            text = cell.Selection is { } selection ? ExcelAnnouncer.Selection(selection) : null;
        else
            text = ExcelAnnouncer.Cell(cell, _lastCell, Options());
        if (cell is not null)
            _lastCell = cell;
        if (!string.IsNullOrWhiteSpace(text))
            _speech.Enqueue(new Utterance(text, interrupt ? SpeechPriority.Interrupt : SpeechPriority.High));
    }

    private ExcelAnnouncementOptions Options()
    {
        var settings = _settings.CurrentValue;
        return new ExcelAnnouncementOptions(settings.ExcelReportFormulas, settings.ExcelReportCommentsAndNotes);
    }

    public override bool TryHandle(NavigationCommand command)
    {
        switch (command)
        {
            case NavigationCommand.ExcelReadFormula:
                WithCell(ExcelAnnouncer.Formula);
                return true;
            case NavigationCommand.ExcelReadComment:
                WithCell(ExcelAnnouncer.CommentAndNote);
                return true;
            case NavigationCommand.ExcelSetColumnTitles:
                WithCell(SetColumnTitles);
                return true;
            case NavigationCommand.ExcelSetRowTitles:
                WithCell(SetRowTitles);
                return true;
            case NavigationCommand.ExcelChartSummary:
                var window = Window;
                _ = Task.Run(async () => Say(ExcelAnnouncer.Chart(await _source.ChartAsync(window).ConfigureAwait(false))));
                return true;
            default:
                return false;
        }
    }

    /// <summary>The current row holds the sheet's column titles; pressed again on that row, no row does.</summary>
    private string SetColumnTitles(ExcelCell cell)
    {
        var (row, _) = _titles.Get(cell.Workbook, cell.Sheet);
        if (row == cell.Row)
        {
            _titles.SetRow(cell.Workbook, cell.Sheet, null);
            return VoxStrings.Get("Excel.ColumnTitlesCleared");
        }
        _titles.SetRow(cell.Workbook, cell.Sheet, cell.Row);
        return VoxStrings.Format("Excel.RowSetAsColumnTitles", cell.Row);
    }

    /// <summary>The current column holds the sheet's row titles; pressed again on that column, no column does.</summary>
    private string SetRowTitles(ExcelCell cell)
    {
        var (_, column) = _titles.Get(cell.Workbook, cell.Sheet);
        if (column == cell.Column)
        {
            _titles.SetColumn(cell.Workbook, cell.Sheet, null);
            return VoxStrings.Get("Excel.RowTitlesCleared");
        }
        _titles.SetColumn(cell.Workbook, cell.Sheet, cell.Column);
        return VoxStrings.Format("Excel.ColumnSetAsRowTitles", ExcelAddress.ColumnLetters(cell.Column));
    }

    /// <summary>Reads the active cell, then says what <paramref name="describe"/> makes of it.</summary>
    private void WithCell(Func<ExcelCell, string> describe)
    {
        var window = Window;
        _ = Task.Run(async () =>
        {
            try
            {
                var cell = await _source.ActiveCellAsync(window).ConfigureAwait(false);
                Say(cell is null ? VoxStrings.Get("Excel.Unavailable") : describe(cell));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Excel command failed");
            }
        });
    }

    private void Say(string text) => _speech.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
}
