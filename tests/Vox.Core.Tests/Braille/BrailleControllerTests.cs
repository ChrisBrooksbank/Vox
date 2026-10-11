using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Braille;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Braille;

public sealed class BrailleControllerTests : IDisposable
{
    private readonly FakeBrailleDisplay _display = new(20);
    private readonly FakeBrailleDisplaySource _source;
    private readonly TestOptionsMonitor<VoxSettings> _settings;
    private readonly Mock<IBrailleRouter> _router = new();
    private readonly Mock<IKeyboardOutput> _keyboard = new();
    private readonly List<string> _typed = [];
    private readonly List<OutputKey> _keys = [];
    private readonly ManualTimeProvider _time = new();
    private readonly List<string> _tables = [];
    private readonly FakeBrailleTranslator _reader = new();
    private BrailleController? _controller;

    private static readonly BrailleObject SaveButton = new() { Name = "save", ControlType = "Button", RuntimeId = [1] };
    private static readonly BrailleObject NameEdit = new() { Name = "name", ControlType = "Edit", RuntimeId = [2] };

    public BrailleControllerTests()
    {
        _source = new FakeBrailleDisplaySource(_display);
        _settings = new TestOptionsMonitor<VoxSettings>(new VoxSettings { BrailleTable = BrailleTables.Uncontracted });
        _keyboard.Setup(k => k.TypeText(It.IsAny<string>())).Callback<string>(_typed.Add);
        _keyboard.Setup(k => k.SendKey(It.IsAny<OutputKey>())).Callback<OutputKey>(_keys.Add);
    }

    public void Dispose() => _controller?.Dispose();

    private BrailleController Controller(Func<VoxSettings, VoxSettings>? configure = null)
    {
        if (configure is not null)
            _settings.Set(configure(_settings.CurrentValue));
        _controller = new BrailleController(_source, _settings, _router.Object, _keyboard.Object,
            NullLogger<BrailleController>.Instance,
            table =>
            {
                _tables.Add(table);
                return new FakeBrailleTranslator();
            },
            _time);
        return _controller;
    }

    /// <summary>What a display shows, read back as text (cursor and selection dots left out).</summary>
    private string Shown(FakeBrailleDisplay? display = null)
    {
        var write = (display ?? _display).Current;
        Assert.NotNull(write);
        return _reader.BackTranslate(write.Cells.Select(c => (byte)(c & 0x3F)).ToArray()).TrimEnd();
    }

    private static BrailleLine Browse(string text, int sourceStart, int? cursor = null) => new()
    {
        Kind = BrailleLineKind.BrowseLine,
        Text = text,
        SourceStart = sourceStart,
        SourceEnd = sourceStart + text.Length,
        CursorOffset = cursor,
    };

    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    // -------------------------------------------------------------------------
    // Lines
    // -------------------------------------------------------------------------

    [Fact]
    public void ShowFocus_ShowsTheFocusLine()
    {
        var controller = Controller();

        controller.ShowFocus(SaveButton, []);

        Assert.Equal("save btn", Shown());
        Assert.Equal(BrailleLineKind.Focus, controller.ShownLine?.Kind);
        Assert.Null(_display.Current!.CursorPosition);
    }

    [Fact]
    public void ShowFocus_WithContext_FillsTheDisplay()
    {
        var controller = Controller();
        var dialog = new BrailleObject { Name = "options", ControlType = "Window", AriaRole = "dialog", RuntimeId = [9] };

        controller.ShowFocus(SaveButton, [dialog]);

        Assert.Equal("options dlg save btn", Shown());
    }

    [Fact]
    public void ShowEditLine_ReplacesTheFocusLine()
    {
        var controller = Controller();
        controller.ShowFocus(NameEdit, []);

        controller.ShowEditLine("hello world", 3, null);

        Assert.Equal("hello world", Shown());
        Assert.Equal(BrailleLineKind.EditLine, controller.ShownLine?.Kind);
        Assert.Equal(3, _display.Current!.CursorPosition);
        Assert.Equal(BrailleDots.Dots78, _display.Current.Cells[3] & BrailleDots.Dots78);
    }

    [Fact]
    public void ShowEditLine_DrawsTheSelection()
    {
        var controller = Controller();

        controller.ShowEditLine("hello world", 5, (0, 5));

        var cells = _display.Current!.Cells;
        Assert.All(cells[..5], c => Assert.Equal(BrailleDots.Dots78, c & BrailleDots.Dots78));
        Assert.Equal(0, cells[6] & BrailleDots.Dots78);
    }

    [Fact]
    public void FocusAncestorsArrivingLater_KeepTheEditLine()
    {
        var controller = Controller();
        controller.ShowFocus(NameEdit, []);
        controller.ShowEditLine("hello", 0, null);

        controller.ShowFocus(NameEdit, [new BrailleObject { Name = "form", ControlType = "Window", RuntimeId = [7] }]);

        Assert.Equal("hello", Shown());
        Assert.Equal(BrailleLineKind.EditLine, controller.ShownLine?.Kind);
    }

    [Fact]
    public void NewFocus_ReplacesTheEditLine()
    {
        var controller = Controller();
        controller.ShowFocus(NameEdit, []);
        controller.ShowEditLine("hello", 0, null);

        controller.ShowFocus(SaveButton, []);

        Assert.Equal("save btn", Shown());
    }

    [Fact]
    public void ShowBrowseLine_ShowsTheLine_AndClearBrowseLineShowsTheFocusAgain()
    {
        var controller = Controller();
        controller.ShowFocus(new BrailleObject { Name = "page", ControlType = "Document", RuntimeId = [3] }, []);

        controller.ShowBrowseLine(Browse("go home lnk now", 100, 0));
        Assert.Equal("go home lnk now", Shown());
        Assert.Equal(BrailleLineKind.BrowseLine, controller.ShownLine?.Kind);

        controller.ClearBrowseLine();
        Assert.Equal("page doc", Shown());
    }

    [Fact]
    public void Writes_AreNotRepeatedWhenNothingChanged()
    {
        var controller = Controller();
        controller.ShowFocus(SaveButton, []);

        controller.ShowFocus(SaveButton, []);

        Assert.Single(_display.Writes);
    }

    // -------------------------------------------------------------------------
    // Tether
    // -------------------------------------------------------------------------

    [Fact]
    public void Automatic_ShowsReviewAfterAReviewCommand_UntilTheCaretMoves()
    {
        var controller = Controller();
        controller.ShowFocus(NameEdit, []);
        controller.ShowEditLine("hello", 0, null);

        controller.ShowReviewLine("review text", 2);
        Assert.Equal("review text", Shown());
        Assert.Equal(BrailleLineKind.Review, controller.ShownLine?.Kind);

        controller.ShowEditLine("hello", 1, null);
        Assert.Equal("hello", Shown());
    }

    [Fact]
    public void Focus_NeverShowsTheReviewCursor()
    {
        var controller = Controller(s => s with { BrailleTether = BrailleTether.Focus });
        controller.ShowEditLine("hello", 0, null);

        controller.ShowReviewLine("review text", 2);

        Assert.Equal("hello", Shown());
        Assert.Equal(BrailleTether.Focus, controller.Tether);
    }

    [Fact]
    public void Review_KeepsShowingTheReviewCursor()
    {
        var controller = Controller(s => s with { BrailleTether = BrailleTether.Review });
        controller.ShowFocus(NameEdit, []);
        Assert.Equal("name edt", Shown()); // no review line yet

        controller.ShowReviewLine("review text", 2);
        controller.ShowEditLine("hello", 0, null);
        controller.ShowFocus(SaveButton, []);

        Assert.Equal("review text", Shown());
    }

    [Fact]
    public void CycleTether_GoesAutomaticFocusReview_AndRaisesTetherChanged()
    {
        var controller = Controller();
        var changes = new List<BrailleTether>();
        controller.TetherChanged += (_, tether) => changes.Add(tether);
        controller.ShowEditLine("hello", 0, null);
        controller.ShowReviewLine("review text", 0);

        controller.CycleTether();
        Assert.Equal("hello", Shown());
        controller.CycleTether();
        Assert.Equal("review text", Shown());
        controller.CycleTether();

        Assert.Equal([BrailleTether.Focus, BrailleTether.Review, BrailleTether.Automatic], changes);
        Assert.Equal(BrailleTether.Automatic, controller.Tether);
    }

    [Fact]
    public void CycleTether_FromTheDisplaysKeys()
    {
        var controller = Controller();
        var changes = new List<BrailleTether>();
        controller.TetherChanged += (_, tether) => changes.Add(tether);

        _display.Press("space", "dot2", "dot4", "dot5");

        Assert.Equal([BrailleTether.Focus], changes);
    }

    [Fact]
    public void TetherSettingChange_ReplacesTheCycledTether()
    {
        var controller = Controller();
        controller.CycleTether(); // Focus

        _settings.Set(_settings.CurrentValue with { BrailleTether = BrailleTether.Review });

        Assert.Equal(BrailleTether.Review, controller.Tether);
    }

    [Fact]
    public void OtherSettingChange_KeepsTheCycledTether()
    {
        var controller = Controller();
        controller.CycleTether(); // Focus

        _settings.Set(_settings.CurrentValue with { BrailleWordWrap = false });

        Assert.Equal(BrailleTether.Focus, controller.Tether);
    }

    [Theory]
    [InlineData(BrailleTether.Automatic, "Braille tethered automatically")]
    [InlineData(BrailleTether.Focus, "Braille tethered to focus")]
    [InlineData(BrailleTether.Review, "Braille tethered to review")]
    public void Describe_SaysTheTether(BrailleTether tether, string expected)
    {
        Assert.Equal(expected, BrailleController.Describe(tether));
    }

    // -------------------------------------------------------------------------
    // Routing
    // -------------------------------------------------------------------------

    [Fact]
    public void Routing_OnAnEditLine_MovesTheCaret()
    {
        var controller = Controller();
        controller.ShowEditLine("hello world", 0, null);

        _display.PressRouting(6);

        _router.Verify(r => r.MoveCaret(6), Times.Once);
    }

    [Fact]
    public void Routing_AfterTheEditLinesEnd_MovesTheCaretToTheEnd()
    {
        var controller = Controller();
        controller.ShowEditLine("hello", 0, null);

        _display.PressRouting(5);
        _display.PressRouting(9);

        _router.Verify(r => r.MoveCaret(5), Times.Once);
        _router.Verify(r => r.MoveCaret(It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void Routing_OnABrowseLine_MovesTheCursor_ThenActivates()
    {
        var controller = Controller();
        controller.ShowBrowseLine(Browse("go home now", 100, 0));

        _display.PressRouting(3);
        _router.Verify(r => r.RouteBrowse(103, false), Times.Once);

        // The cursor moved there: pressed again, the element is activated
        controller.ShowBrowseLine(Browse("go home now", 100, 3));
        _display.PressRouting(3);
        _router.Verify(r => r.RouteBrowse(103, true), Times.Once);
    }

    [Fact]
    public void Routing_OnABrowseLabel_GoesToItsElement()
    {
        var controller = Controller();
        var line = Browse("go home lnk now", 100, 0) with
        {
            SourceOffsets = [100, 101, 102, 103, 104, 105, 106, 103, 103, 103, 103, 107, 108, 109, 110],
            SourceEnd = 111,
        };
        controller.ShowBrowseLine(line);

        _display.PressRouting(9); // the "n" of "lnk"

        _router.Verify(r => r.RouteBrowse(103, false), Times.Once);
    }

    [Fact]
    public void Routing_OnTheFocusLine_ActivatesIt()
    {
        var controller = Controller();
        controller.ShowFocus(SaveButton, []);

        _display.PressRouting(1);

        _router.Verify(r => r.ActivateFocus(), Times.Once);
    }

    [Fact]
    public void Routing_PastTheText_DoesNothing()
    {
        var controller = Controller();
        controller.ShowFocus(SaveButton, []);

        _display.PressRouting(15);

        _router.VerifyNoOtherCalls();
    }

    [Fact]
    public void Routing_OnTheReviewLine_MovesTheReviewCursor()
    {
        var controller = Controller();
        controller.ShowReviewLine("review text", 0);

        _display.PressRouting(7);

        _router.Verify(r => r.MoveReviewCursor(7), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Panning
    // -------------------------------------------------------------------------

    // On 20 cells: "one two three four " | "five six seven"
    private const string LongLine = "one two three four five six seven";

    [Fact]
    public void PanRight_MovesThroughTheLine_ThenToTheNextLine()
    {
        var controller = Controller();
        controller.ShowBrowseLine(Browse(LongLine, 0, 0));
        Assert.Equal("one two three four", Shown());

        _display.Press("panRight");
        Assert.Equal("five six seven", Shown());
        _router.Verify(r => r.RunCommand(It.IsAny<NavigationCommand>()), Times.Never);

        _display.Press("panRight");
        _router.Verify(r => r.RunCommand(NavigationCommand.NextLine), Times.Once);
    }

    [Fact]
    public void PanLeft_AtTheStart_GoesToThePreviousLine_ShownFromItsEnd()
    {
        var controller = Controller();
        controller.ShowBrowseLine(Browse("last line", 200, 0));

        _display.Press("panLeft");
        _router.Verify(r => r.RunCommand(NavigationCommand.PrevLine), Times.Once);

        controller.ShowBrowseLine(Browse(LongLine, 100, 0));
        Assert.Equal("five six seven", Shown());
    }

    [Fact]
    public void PanLeft_ThePreviousLineArrivingLate_IsShownFromItsStart()
    {
        var controller = Controller();
        controller.ShowBrowseLine(Browse("last line", 200, 0));

        _display.Press("panLeft");
        _time.Advance(TimeSpan.FromSeconds(2));
        controller.ShowBrowseLine(Browse(LongLine, 100, 0));

        Assert.Equal("one two three four", Shown());
    }

    [Fact]
    public void PanPastAnEditLine_SendsArrowKeys()
    {
        var controller = Controller();
        controller.ShowEditLine("hello", 0, null);

        _display.Press("panRight");
        _display.Press("panLeft");

        Assert.Equal([new OutputKey(0x28), new OutputKey(0x26)], _keys);
    }

    [Fact]
    public void PanPastAReviewLine_RunsReviewLineCommands()
    {
        var controller = Controller();
        controller.ShowReviewLine("review", 0);

        _display.Press("panRight");
        _display.Press("rockerUp");

        _router.Verify(r => r.RunCommand(NavigationCommand.ReviewNextLine), Times.Once);
        _router.Verify(r => r.RunCommand(NavigationCommand.ReviewPrevLine), Times.Once);
    }

    [Fact]
    public void PanPastTheFocusLine_DoesNothing()
    {
        var controller = Controller();
        controller.ShowFocus(SaveButton, []);

        _display.Press("panRight");
        _display.Press("panLeft");

        _router.VerifyNoOtherCalls();
        Assert.Empty(_keys);
    }

    // -------------------------------------------------------------------------
    // Messages
    // -------------------------------------------------------------------------

    [Fact]
    public void OfferMessage_IsShownAfterTheQuietPeriod_ThenTimesOut()
    {
        var controller = Controller();
        controller.ShowFocus(SaveButton, []);
        _time.Advance(TimeSpan.FromSeconds(1));

        controller.OfferMessage("file saved");
        _time.Advance(Ms(100));
        Assert.Equal("save btn", Shown());

        _time.Advance(Ms(100));
        Assert.Equal("file saved", Shown());
        Assert.Equal(BrailleLineKind.Message, controller.ShownLine?.Kind);
        Assert.Null(_display.Current!.CursorPosition);

        _time.Advance(TimeSpan.FromSeconds(3.5));
        Assert.Equal("file saved", Shown());
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("save btn", Shown());
        Assert.Equal(BrailleLineKind.Focus, controller.ShownLine?.Kind);
    }

    [Fact]
    public void OfferMessage_WithALineUpdateJustAfter_IsThatLinesSpeech()
    {
        var controller = Controller();
        controller.ShowFocus(SaveButton, []);
        _time.Advance(TimeSpan.FromSeconds(1));

        controller.OfferMessage("name edit");
        _time.Advance(Ms(50));
        controller.ShowFocus(NameEdit, []);
        _time.Advance(Ms(500));

        Assert.Equal("name edt", Shown());
        Assert.Equal(BrailleLineKind.Focus, controller.ShownLine?.Kind);
    }

    [Fact]
    public void OfferMessage_WithALineUpdateJustBefore_IsThatLinesSpeech()
    {
        var controller = Controller();
        controller.ShowFocus(SaveButton, []);
        _time.Advance(Ms(50));

        controller.OfferMessage("save button");
        _time.Advance(Ms(500));

        Assert.Equal("save btn", Shown());
    }

    [Fact]
    public void OfferMessage_TurnsStateMarksIntoWords_AndFlattensLines()
    {
        var controller = Controller();

        controller.OfferMessage($"box {StateSounds.Mark(StateSounds.Checked, "checked")}\nnext");
        _time.Advance(Ms(200));

        Assert.Equal("box checked next", Shown());
    }

    [Fact]
    public void OfferMessage_IsIgnored_WithNoTimeoutOrNoDisplay()
    {
        var controller = Controller(s => s with { BrailleMessageTimeoutSeconds = 0 });
        controller.OfferMessage("hello");
        _time.Advance(Ms(200));
        Assert.Empty(_display.Writes);

        _settings.Set(_settings.CurrentValue with { BrailleMessageTimeoutSeconds = 4 });
        _display.IsConnected = false;
        controller.OfferMessage("hello");
        _time.Advance(Ms(200));
        Assert.Null(controller.ShownLine);
    }

    [Fact]
    public void PanningAMessage_KeepsItUp()
    {
        var controller = Controller();
        controller.ShowFocus(SaveButton, []);
        controller.ShowMessage("this message is longer than the display can show");
        var first = Shown();

        _time.Advance(TimeSpan.FromSeconds(3));
        _display.Press("panRight");
        Assert.NotEqual(first, Shown());
        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(BrailleLineKind.Message, controller.ShownLine?.Kind);

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("save btn", Shown());
    }

    [Fact]
    public void PanningPastAMessagesEnd_DismissesIt()
    {
        var controller = Controller();
        controller.ShowFocus(SaveButton, []);
        controller.ShowMessage("short");

        _display.Press("panRight");

        Assert.Equal("save btn", Shown());
        _router.VerifyNoOtherCalls();
    }

    [Fact]
    public void Routing_OnAMessage_DismissesIt()
    {
        var controller = Controller();
        controller.ShowFocus(SaveButton, []);
        controller.ShowMessage("short");

        _display.PressRouting(0);

        Assert.Equal("save btn", Shown());
        _router.VerifyNoOtherCalls();
    }

    [Fact]
    public void ALineUpdate_ReplacesAMessage()
    {
        var controller = Controller();
        controller.ShowMessage("short");

        controller.ShowEditLine("typed", 5, null);

        Assert.Equal("typed", Shown());
        Assert.Equal(BrailleLineKind.EditLine, controller.ShownLine?.Kind);
    }

    // -------------------------------------------------------------------------
    // Cursor blink
    // -------------------------------------------------------------------------

    [Fact]
    public void Cursor_BlinksOnThePeriod()
    {
        var controller = Controller();
        controller.ShowEditLine("hello", 2, null);
        Assert.Equal(BrailleDots.Dots78, _display.Current!.Cells[2] & BrailleDots.Dots78);

        _time.Advance(Ms(500));
        Assert.Equal(0, _display.Current!.Cells[2] & BrailleDots.Dots78);
        Assert.Equal(2, _display.Current.CursorPosition);

        _time.Advance(Ms(500));
        Assert.Equal(BrailleDots.Dots78, _display.Current!.Cells[2] & BrailleDots.Dots78);
        Assert.Equal(3, _display.Writes.Count);
    }

    [Fact]
    public void Cursor_ShapeAndRateFromSettings()
    {
        var controller = Controller(s => s with { BrailleCursorShape = BrailleCursorShape.AllDots, BrailleCursorBlinkRateMs = 1000 });
        controller.ShowEditLine("hello", 2, null);
        Assert.Equal(0xFF, _display.Current!.Cells[2]);

        _time.Advance(Ms(500));
        Assert.Single(_display.Writes);
        _time.Advance(Ms(500));
        Assert.Equal(2, _display.Writes.Count);
    }

    [Fact]
    public void Cursor_WithoutBlink_StaysUp()
    {
        var controller = Controller(s => s with { BrailleCursorBlink = false });
        controller.ShowEditLine("hello", 2, null);

        _time.Advance(TimeSpan.FromSeconds(3));

        Assert.Single(_display.Writes);
        Assert.Equal(BrailleDots.Dots78, _display.Current!.Cells[2] & BrailleDots.Dots78);
    }

    [Fact]
    public void Cursor_AMoveShowsItAtOnce()
    {
        var controller = Controller();
        controller.ShowEditLine("hello", 2, null);
        _time.Advance(Ms(500)); // hidden

        controller.ShowEditLine("hello", 3, null);

        Assert.Equal(BrailleDots.Dots78, _display.Current!.Cells[3] & BrailleDots.Dots78);
    }

    // -------------------------------------------------------------------------
    // Displays and settings
    // -------------------------------------------------------------------------

    [Fact]
    public void ADisplayWithOtherCells_GetsTheLineAgain()
    {
        var controller = Controller();
        controller.ShowEditLine("hello world", 0, null);
        var small = new FakeBrailleDisplay(8);

        _source.Change(small);

        Assert.Equal("hello", Shown(small));
        Assert.Equal(8, small.Current!.Cells.Length);
    }

    [Fact]
    public void ADisplayReconnected_GetsTheLineAgain()
    {
        var controller = Controller();
        controller.ShowFocus(SaveButton, []);
        Assert.True(controller.IsActive);

        _display.IsConnected = false;
        _source.RaiseChanged();
        Assert.False(controller.IsActive);
        controller.ShowFocus(NameEdit, []);
        Assert.Single(_display.Writes);

        _display.IsConnected = true;
        _source.RaiseChanged();

        Assert.Equal(2, _display.Writes.Count);
        Assert.Equal("name edt", Shown());
    }

    [Fact]
    public void ADisplayConnected_ShowsTheSameCellsAgain()
    {
        var controller = Controller();
        controller.ShowFocus(SaveButton, []);

        _source.RaiseChanged();

        Assert.Equal(2, _display.Writes.Count);
    }

    [Fact]
    public void TableSettingChange_MakesANewTranslator()
    {
        var controller = Controller();
        controller.ShowFocus(SaveButton, []);
        Assert.Equal([BrailleTables.Uncontracted], _tables);

        _settings.Set(_settings.CurrentValue with { BrailleTable = BrailleTables.Contracted });

        Assert.Equal([BrailleTables.Uncontracted, BrailleTables.Contracted], _tables);
        Assert.Equal(2, _display.Writes.Count);
        Assert.Equal("save btn", Shown());

        _settings.Set(_settings.CurrentValue with { BrailleWordWrap = false });
        Assert.Equal(2, _tables.Count);
    }

    // -------------------------------------------------------------------------
    // Keys
    // -------------------------------------------------------------------------

    [Fact]
    public void ChordBoundToACommand_RunsIt()
    {
        Controller();

        _display.Press("dot5", "space", "dot1", "dot2");

        _router.Verify(r => r.RunCommand(NavigationCommand.NextHeading), Times.Once);
    }

    [Fact]
    public void ChordBoundToAKey_SendsIt()
    {
        Controller();

        _display.Press("space", "dot4", "dot5");

        Assert.Equal([new OutputKey(0x09)], _keys);
    }

    [Fact]
    public void Dots_AreTyped_Uncontracted()
    {
        Controller();

        _display.Press("dot1", "dot4");
        _display.Press("space");

        Assert.Equal(["c", " "], _typed);
    }

    [Fact]
    public void Dots_AreKeptUntilTheWordEnds_Contracted()
    {
        Controller(s => s with { BrailleTable = BrailleTables.Contracted });

        _display.Press("dot1", "dot4");
        _display.Press("dot1");
        Assert.Empty(_typed);

        _display.Press("dot8"); // Enter
        Assert.Equal(["ca"], _typed);
        Assert.Equal([new OutputKey(0x0D)], _keys);
    }

    [Fact]
    public void NewFocus_DiscardsAWordBeingTyped()
    {
        var controller = Controller(s => s with { BrailleTable = BrailleTables.Contracted });
        controller.ShowFocus(NameEdit, []);
        _display.Press("dot1");

        controller.ShowFocus(SaveButton, []);
        _display.Press("space");

        Assert.Equal([" "], _typed);
    }

    [Fact]
    public void ACommand_TypesTheWordFirst()
    {
        Controller(s => s with { BrailleTable = BrailleTables.Contracted });
        _display.Press("dot1");

        _display.Press("space", "dot4", "dot5"); // Tab

        Assert.Equal(["a"], _typed);
        Assert.Equal([new OutputKey(0x09)], _keys);
    }

    [Fact]
    public void BrlttyCharacter_IsTyped()
    {
        Controller();

        _display.Press("char_x");

        Assert.Equal(["x"], _typed);
    }

    [Fact]
    public void Dispose_StopsListening()
    {
        var controller = Controller();
        controller.Dispose();

        _display.Press("space", "dot2", "dot4", "dot5");
        _settings.Set(_settings.CurrentValue with { BrailleTable = BrailleTables.Contracted });

        Assert.Equal(BrailleTether.Automatic, controller.Tether);
        Assert.Single(_tables);
    }
}
