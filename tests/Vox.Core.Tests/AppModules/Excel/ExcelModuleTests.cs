using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.AppModules;
using Vox.Core.AppModules.Excel;
using Vox.Core.AppModules.Office;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.AppModules.Excel;

public class ExcelModuleTests : IDisposable
{
    /// <summary>Runs posted pipeline work at once, as the pipeline thread would.</summary>
    private sealed class InlineSink : IEventSink
    {
        public void Post(ScreenReaderEvent evt)
        {
            if (evt is PipelineWorkEvent work)
                lock (this) work.Work();
        }
    }

    private readonly FakeOfficeObjectModel _office = new();
    private readonly FakeExcelApp _excel = new();
    private readonly ExcelTitleStore _titles = new();
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _speech;
    private readonly TestOptionsMonitor<VoxSettings> _settings = new(new VoxSettings());
    private readonly ExcelModule _module;

    public ExcelModuleTests()
    {
        _office.Applications[OfficeApp.Excel] = _excel;
        _speech = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        _module = new ExcelModule(new ExcelObjectModelSource(_office, _titles), _titles, _speech, new InlineSink(), _settings);
        _module.OnActivated(new AppActivation(1, "EXCEL", new IntPtr(5)));
    }

    public void Dispose() => _speech.Dispose();

    private static FocusChangedEvent CellFocus(string name = "B5") => new(DateTimeOffset.UtcNow, name, "DataItem");

    private void MoveTo(int row, int column, string text) =>
        _excel.ActiveCell = new FakeRange { Row = row, Column = column, Text = text };

    [Fact]
    public void ReadCell_ReadsEverythingTheCellHas()
    {
        _excel.ActiveSheet.AutoFilterMode = true;
        _excel.ActiveSheet.AutoFilter = new FakeAutoFilter { Range = new FakeRange { Row = 1, Column = 1, Columns = new FakeCount(3) } };
        _excel.ActiveSheet.AutoFilter.FiltersOn.Add(2);
        _excel.ActiveCell = new FakeRange
        {
            Row = 1, Column = 2, Text = "Month", HasFormula = true, Formula = "=X1",
            Comment = new FakeComment("A note"), CommentThreaded = new FakeComment("A comment"),
            Validation = new FakeValidation { InputTitle = "Month", InputMessage = "Pick one" },
            MergeCells = true, MergeArea = new FakeRange { RangeAddress = "B1:C1" },
        };

        var cell = ExcelObjectModelSource.ReadCell(_excel, _titles)!;

        Assert.Equal(new ExcelCell("C:\\Budget.xlsx", "Budget", 1, 2, "Month", true, "=X1", "A comment", "A note",
            "Month", "Pick one", HasFilter: true, MergedArea: "B1:C1"), cell);
    }

    [Fact]
    public void ReadCell_NoValidationOrFilter_LeavesThemOut()
    {
        MoveTo(3, 3, "7");

        var cell = ExcelObjectModelSource.ReadCell(_excel, _titles)!;

        Assert.Null(cell.ValidationMessage);
        Assert.False(cell.HasFilter);
        Assert.Null(cell.Selection);
    }

    [Fact]
    public void ReadCell_ReadsTheTitlesSetForTheSheet()
    {
        _titles.SetRow("C:\\Budget.xlsx", "Budget", 1);
        _titles.SetColumn("C:\\Budget.xlsx", "Budget", 1);
        _excel.ActiveSheet.Texts[(1, 3)] = "March";
        _excel.ActiveSheet.Texts[(4, 1)] = "Rent";
        MoveTo(4, 3, "500");

        var cell = ExcelObjectModelSource.ReadCell(_excel, _titles)!;

        Assert.Equal("March", cell.ColumnTitle);
        Assert.Equal("Rent", cell.RowTitle);
        // On the title row itself the column title isn't said
        MoveTo(1, 3, "March");
        Assert.Null(ExcelObjectModelSource.ReadCell(_excel, _titles)!.ColumnTitle);
    }

    [Fact]
    public void ReadCell_SeveralCellsSelected_ReadsTheSelection()
    {
        MoveTo(1, 1, "x");
        _excel.Selection = new FakeRange { RangeAddress = "A1:B2", CountLarge = 4, Rows = new FakeCount(2), Columns = new FakeCount(2) };

        Assert.Equal(new ExcelSelection("A1:B2", 2, 2, 4), ExcelObjectModelSource.ReadCell(_excel, _titles)!.Selection);
    }

    [Fact]
    public void ReadChart_ReadsTheFirstChartOfTheSheet()
    {
        var chart = new FakeChart { HasTitle = true, ChartTitle = new FakeChartTitle { Text = "Sales" }, ChartType = 4 };
        chart.SeriesNames.AddRange(["North", "South"]);
        _excel.ActiveSheet.Charts.Add(chart);

        Assert.Equal("Chart Sales, line chart, 2 series: North, South",
            ExcelAnnouncer.Chart(ExcelObjectModelSource.ReadChart(_excel)));
        Assert.Null(ExcelObjectModelSource.ReadChart(new FakeExcelApp()));
    }

    [Fact]
    public async Task CellFocus_IsReplacedByTheObjectModelsAnnouncement()
    {
        MoveTo(5, 2, "42");

        Assert.Null(_module.FormatFocusAnnouncement(CellFocus(), "B5, grid item"));

        var spoken = await _engine.WaitForTextAsync("42, B5");
        // The first cell is queued after the window's title, not over it
        Assert.Equal(SpeechPriority.High, spoken.Priority);
    }

    [Fact]
    public async Task MovingFromCellToCell_Interrupts()
    {
        MoveTo(5, 2, "42");
        _module.FormatFocusAnnouncement(CellFocus(), "B5");
        await _engine.WaitForTextAsync("42, B5");

        MoveTo(6, 2, "43");
        _module.FormatFocusAnnouncement(CellFocus("B6"), "B6");

        Assert.Equal(SpeechPriority.Interrupt, (await _engine.WaitForTextAsync("43, B6")).Priority);
    }

    [Fact]
    public async Task ExcelBusy_SaysVoxsOwnAnnouncement()
    {
        _office.Applications.Clear();

        _module.FormatFocusAnnouncement(CellFocus(), "B5, grid item");

        await _engine.WaitForTextAsync("B5, grid item");
    }

    [Fact]
    public async Task ALateAnswer_ForACellAlreadyLeft_IsDropped()
    {
        var gate = new TaskCompletionSource();
        _office.Gate = gate.Task;
        MoveTo(5, 2, "old");
        _module.FormatFocusAnnouncement(CellFocus(), "B5");
        _office.Gate = null;
        MoveTo(6, 2, "new");
        _module.FormatFocusAnnouncement(CellFocus("B6"), "B6");
        await _engine.WaitForTextAsync("new, B6");

        gate.SetResult();
        await Task.Delay(100);

        Assert.DoesNotContain(_engine.SpokenText, t => t.Contains("old"));
    }

    [Fact]
    public void OtherControls_KeepVoxsAnnouncement() =>
        Assert.Equal("Name box, edit", _module.FormatFocusAnnouncement(new FocusChangedEvent(DateTimeOffset.UtcNow, "Name box", "Edit"), "Name box, edit"));

    [Fact]
    public async Task ReadFormula_Command()
    {
        _excel.ActiveCell = new FakeRange { Row = 2, Column = 2, Text = "4", HasFormula = true, Formula = "=A2*2" };

        Assert.True(_module.TryHandle(NavigationCommand.ExcelReadFormula));

        await _engine.WaitForTextAsync("B2 formula =A2*2");
    }

    [Fact]
    public async Task SetColumnTitles_TogglesForTheCurrentRow()
    {
        MoveTo(1, 2, "Month");

        _module.TryHandle(NavigationCommand.ExcelSetColumnTitles);
        await _engine.WaitForTextAsync("Row 1 set as column titles");
        Assert.Equal((1, (int?)null), _titles.Get("C:\\Budget.xlsx", "Budget"));

        _module.TryHandle(NavigationCommand.ExcelSetColumnTitles);
        await _engine.WaitForTextAsync("Column titles cleared");
        Assert.Equal(((int?)null, (int?)null), _titles.Get("C:\\Budget.xlsx", "Budget"));
    }

    [Fact]
    public async Task SetRowTitles_NamesTheColumn()
    {
        MoveTo(3, 1, "Rent");

        _module.TryHandle(NavigationCommand.ExcelSetRowTitles);

        await _engine.WaitForTextAsync("Column A set as row titles");
    }

    [Fact]
    public void OtherCommands_AreNotTheModules() =>
        Assert.False(_module.TryHandle(NavigationCommand.SayAll));

    [Fact]
    public async Task Settings_TurnOffTheFormulaIndicator()
    {
        _settings.Set(new VoxSettings { ExcelReportFormulas = false });
        _excel.ActiveCell = new FakeRange { Row = 2, Column = 2, Text = "4", HasFormula = true };

        _module.FormatFocusAnnouncement(CellFocus(), "B2");

        await _engine.WaitForTextAsync("4, B2");
    }

    [Fact]
    public void KeyMap_BindsEveryExcelCommand()
    {
        var bound = AppModuleKeyMaps.Load("excel").Select(b => b.Command).ToHashSet();

        foreach (var command in new[] { NavigationCommand.ExcelReadFormula, NavigationCommand.ExcelReadComment,
                     NavigationCommand.ExcelSetColumnTitles, NavigationCommand.ExcelSetRowTitles, NavigationCommand.ExcelChartSummary })
            Assert.Contains(command.ToString(), bound);
    }
}
