namespace Vox.Core.Tests.AppModules.Excel;

// Plain stand-ins for Excel's object model, read late bound by ExcelObjectModelSource as COM
// objects would be. Only the members Vox reads.

public sealed class FakeExcelApp
{
    public FakeWorkbook ActiveWorkbook { get; set; } = new();
    public FakeSheet ActiveSheet { get; set; } = new();
    public FakeRange? ActiveCell { get; set; }
    public FakeRange? Selection { get; set; }
    public FakeChart? ActiveChart { get; set; }
}

public sealed class FakeWorkbook
{
    public string FullName { get; set; } = "C:\\Budget.xlsx";
}

public sealed class FakeCount(int count)
{
    public int Count { get; } = count;
}

public sealed class FakeSheet
{
    public string Name { get; set; } = "Budget";
    public Dictionary<(int Row, int Column), string> Texts { get; } = new();
    public bool AutoFilterMode { get; set; }
    public FakeAutoFilter? AutoFilter { get; set; }
    public List<FakeChart> Charts { get; } = [];

    public FakeRange Cells(int row, int column) =>
        new() { Row = row, Column = column, Text = Texts.GetValueOrDefault((row, column), string.Empty) };

    public FakeChartObjects ChartObjects() => new(Charts);
}

public sealed class FakeAutoFilter
{
    public FakeRange Range { get; set; } = new();
    public HashSet<int> FiltersOn { get; } = [];
    public FakeFilter Filters(int index) => new() { On = FiltersOn.Contains(index) };
}

public sealed class FakeFilter
{
    public bool On { get; set; }
}

public sealed class FakeComment(string text)
{
    public string Text() => text;
}

public sealed class FakeValidation
{
    public string InputTitle { get; set; } = string.Empty;
    public string InputMessage { get; set; } = string.Empty;
    public bool ShowInput { get; set; } = true;
}

public sealed class FakeRange
{
    public int Row { get; set; } = 1;
    public int Column { get; set; } = 1;
    public string Text { get; set; } = string.Empty;
    public bool HasFormula { get; set; }
    public string Formula { get; set; } = string.Empty;
    public FakeComment? Comment { get; set; }
    public FakeComment? CommentThreaded { get; set; }
    public bool MergeCells { get; set; }
    public FakeRange? MergeArea { get; set; }
    public string RangeAddress { get; set; } = "A1";
    public long CountLarge { get; set; } = 1;
    public FakeCount Rows { get; set; } = new(1);
    public FakeCount Columns { get; set; } = new(1);

    private FakeValidation? _validation;

    /// <summary>Excel throws reading the validation of a cell without one.</summary>
    public FakeValidation Validation
    {
        get => _validation ?? throw new InvalidOperationException("No validation");
        set => _validation = value;
    }

    public string Address(bool rowAbsolute, bool columnAbsolute) => RangeAddress;
}

public sealed class FakeChartObjects(List<FakeChart> charts)
{
    public int Count => charts.Count;
    public FakeChartObject Item(int index) => new(charts[index - 1]);
}

public sealed class FakeChartObject(FakeChart chart)
{
    public FakeChart Chart { get; } = chart;
}

public sealed class FakeChart
{
    public bool HasTitle { get; set; }
    public FakeChartTitle ChartTitle { get; set; } = new();
    public int ChartType { get; set; } = 51;
    public List<string> SeriesNames { get; } = [];
    public FakeSeriesCollection SeriesCollection() => new(SeriesNames);
}

public sealed class FakeChartTitle
{
    public string Text { get; set; } = string.Empty;
}

public sealed class FakeSeriesCollection(List<string> names)
{
    public int Count => names.Count;
    public FakeSeries Item(int index) => new() { Name = names[index - 1] };
}

public sealed class FakeSeries
{
    public string Name { get; set; } = string.Empty;
}
