using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.AppModules;
using Vox.Core.AppModules.Word;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.AppModules.Word;

public class WordAnnouncerTests
{
    [Fact]
    public void Heading_TextThenLevel() =>
        Assert.Equal("Results, heading level 2",
            WordAnnouncer.Element(new WordElement(WordElementKind.Heading, "Results\r", Level: 2)));

    [Fact]
    public void Table_SizeThenFirstCell() =>
        Assert.Equal("table with 3 rows, 1 column, Name",
            WordAnnouncer.Element(new WordElement(WordElementKind.Table, "Name\r\a", Rows: 3, Columns: 1)));

    [Fact]
    public void List_ItemCountThenFirstItem() =>
        Assert.Equal("list with 4 items, Apples",
            WordAnnouncer.Element(new WordElement(WordElementKind.List, "Apples\r", Items: 4)));

    [Fact]
    public void SpellingError() =>
        Assert.Equal("teh, misspelled", WordAnnouncer.Element(new WordElement(WordElementKind.SpellingError, "teh")));

    [Fact]
    public void CommentAndRevision()
    {
        Assert.Equal("Comment by Ann: Check this figure", WordAnnouncer.Comment(new WordComment("Ann", "Check this figure")));
        Assert.Equal("Insertion by Bob: new words", WordAnnouncer.Revision(new WordRevision(1, "Bob", "new words")));
        Assert.Equal("Deletion by unknown author: old", WordAnnouncer.Revision(new WordRevision(2, "", "old")));
        // Formatting changes don't read out the formatted text
        Assert.Equal("Property change by Bob", WordAnnouncer.Revision(new WordRevision(3, "Bob", "bold text")));
    }

    [Fact]
    public void NotFound() =>
        Assert.Equal("No previous tracked change", WordAnnouncer.NotFound(WordElementKind.Revision, forward: false));

    [Fact]
    public void Entered_SaysOnlyWhatTheCaretWasNotInBefore()
    {
        var insertion = new WordRevision(1, "Bob", "new");
        var comment = new WordComment("Ann", "Why?");
        var before = new WordCaretContext([], [insertion]);
        var now = new WordCaretContext([comment], [insertion]);

        Assert.Equal("Comment by Ann: Why?", WordAnnouncer.Entered(now, before));
        Assert.Null(WordAnnouncer.Entered(now, now));
        Assert.Null(WordAnnouncer.Entered(WordCaretContext.Empty, now));
        Assert.Equal("Insertion by Bob: new. Comment by Ann: Why?", WordAnnouncer.Entered(now, WordCaretContext.Empty));
    }

    [Fact]
    public void Location()
    {
        Assert.Equal("Page 3 of 10, section 1, line 12, column 5", WordAnnouncer.Location(new WordLocation(3, 1, 12, 5, 10)));
        Assert.Equal("Page 3, line 12", WordAnnouncer.Location(new WordLocation(3, 0, 12, 0)));
        Assert.Equal("Location not available", WordAnnouncer.Location(new WordLocation(0, 0, 0, 0)));
    }

    [Fact]
    public void Cell_SaysTheHeaderOfWhatChanged()
    {
        var cell = new WordTableCell("£20\r\a", 3, 2, 5, 4, ColumnHeader: "Price", RowHeader: "Bread");

        Assert.Equal("Price, £20, row 3, column 2", WordAnnouncer.Cell(cell, WordTableMove.Right));
        Assert.Equal("Bread, £20, row 3, column 2", WordAnnouncer.Cell(cell, WordTableMove.Down));
        // In the header row itself, no column header
        Assert.Equal("Price, row 1, column 2", WordAnnouncer.Cell(cell with { Text = "Price", Row = 1 }, WordTableMove.Right));
        Assert.Equal("blank, row 3, column 1", WordAnnouncer.Cell(cell with { Text = "\r\a", Column = 1 }, WordTableMove.Down));
    }
}

public class WordModuleTests : IDisposable
{
    private sealed class FakeWordSource : IWordSource
    {
        public WordLocation? Location { get; set; }
        public Func<WordElementKind, bool, WordMoveResult?> Move { get; set; } = (_, _) => new WordMoveResult(null);
        public Func<WordTableMove, WordTableResult?> Table { get; set; } = _ => new WordTableResult(false, null);
        public WordCaretContext? Context { get; set; } = WordCaretContext.Empty;
        public List<(WordElementKind, bool)> Moves { get; } = [];

        public Task<WordLocation?> LocationAsync(IntPtr window) => Task.FromResult(Location);

        public Task<WordMoveResult?> MoveToAsync(IntPtr window, WordElementKind kind, bool forward)
        {
            Moves.Add((kind, forward));
            return Task.FromResult(Move(kind, forward));
        }

        public Task<WordCaretContext?> CaretContextAsync(IntPtr window) => Task.FromResult(Context);

        public Task<WordTableResult?> MoveInTableAsync(IntPtr window, WordTableMove move) => Task.FromResult(Table(move));
    }

    private sealed class InlineSink : IEventSink
    {
        public void Post(ScreenReaderEvent evt)
        {
            if (evt is PipelineWorkEvent work)
                lock (this) work.Work();
        }
    }

    private readonly FakeWordSource _source = new();
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _speech;
    private readonly TestOptionsMonitor<VoxSettings> _settings = new(new VoxSettings());
    private readonly WordModule _module;

    public WordModuleTests()
    {
        _speech = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        _module = new WordModule(_source, _speech, new InlineSink(), _settings);
        _module.OnActivated(new AppActivation(1, "WINWORD", new IntPtr(7)));
    }

    public void Dispose() => _speech.Dispose();

    [Fact]
    public async Task QuickNavigation_MovesAndReads()
    {
        _source.Move = (kind, _) => new WordMoveResult(new WordElement(kind, "Summary", Level: 1));

        Assert.True(_module.TryHandle(NavigationCommand.WordNextHeading));

        await _engine.WaitForTextAsync("Summary, heading level 1");
        Assert.Equal([(WordElementKind.Heading, true)], _source.Moves);
    }

    [Fact]
    public async Task QuickNavigation_NoneThatWay()
    {
        _module.TryHandle(NavigationCommand.WordPrevSpellingError);

        await _engine.WaitForTextAsync("No previous spelling error");
    }

    [Fact]
    public async Task QuickNavigation_WordBusy()
    {
        _source.Move = (_, _) => null;

        _module.TryHandle(NavigationCommand.WordNextTable);

        await _engine.WaitForTextAsync("Not available while Word is busy");
    }

    [Fact]
    public void EveryQuickNavigationCommand_IsHandledAndBound()
    {
        var bound = AppModuleKeyMaps.Load("word").Select(b => b.Command).ToHashSet();
        foreach (var command in WordModule.QuickNavigation.Keys)
        {
            Assert.True(_module.TryHandle(command));
            Assert.Contains(command.ToString(), bound);
        }
        Assert.Equal(12, WordModule.QuickNavigation.Count);
    }

    [Fact]
    public async Task Location_Command()
    {
        _source.Location = new WordLocation(2, 1, 4, 9, 5);

        Assert.True(_module.TryHandle(NavigationCommand.WordLocation));

        await _engine.WaitForTextAsync("Page 2 of 5, section 1, line 4, column 9");
    }

    [Fact]
    public async Task TableNavigation()
    {
        _source.Table = move => move == WordTableMove.Right
            ? new WordTableResult(true, new WordTableCell("10", 2, 2, 3, 3, "Qty", "Pens"))
            : new WordTableResult(true, null);

        _module.TryHandle(NavigationCommand.WordTableCellRight);
        await _engine.WaitForTextAsync("Qty, 10, row 2, column 2");

        _module.TryHandle(NavigationCommand.WordTableCellUp);
        await _engine.WaitForTextAsync("Top of column");
    }

    [Fact]
    public async Task TableNavigation_OutsideATable()
    {
        _module.TryHandle(NavigationCommand.WordTableCellDown);

        await _engine.WaitForTextAsync("Not in a table");
    }

    [Fact]
    public async Task CaretMove_IntoACommentOrChange_IsSaid_OnceOnly()
    {
        var caret = new CaretMovedEvent(DateTimeOffset.UtcNow, [1]);
        _source.Context = new WordCaretContext([new WordComment("Ann", "Why?")], []);

        _module.OnEvent(caret);
        var spoken = await _engine.WaitForTextAsync("Comment by Ann: Why?");
        Assert.Equal(SpeechPriority.Normal, spoken.Priority);

        // Still inside it: nothing more
        _module.OnEvent(caret);
        await Task.Delay(100);
        Assert.Single(_engine.SpokenText);
    }

    [Fact]
    public async Task CaretMove_SettingOff_SaysNothing()
    {
        _settings.Set(new VoxSettings { WordReportCommentsAndRevisions = false });
        _source.Context = new WordCaretContext([new WordComment("Ann", "Why?")], []);

        _module.OnEvent(new CaretMovedEvent(DateTimeOffset.UtcNow, [1]));
        await Task.Delay(100);

        Assert.Empty(_engine.SpokenText);
    }

    [Fact]
    public void OtherCommands_AreNotTheModules() =>
        Assert.False(_module.TryHandle(NavigationCommand.ExcelReadFormula));
}
