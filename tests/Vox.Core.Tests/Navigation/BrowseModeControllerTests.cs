using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Audio;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.Buffer;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class BrowseModeControllerTests : IDisposable
{
    private sealed class CaptureSink : IEventSink
    {
        private readonly List<ScreenReaderEvent> _events = new();
        public void Post(ScreenReaderEvent evt) { lock (_events) _events.Add(evt); }
        public List<T> OfType<T>() { lock (_events) return _events.OfType<T>().ToList(); }
    }

    private readonly List<Utterance> _spoken = new();
    private readonly Mock<ISpeechEngine> _engine = new();
    private readonly SpeechQueue _speechQueue;
    private readonly Mock<IAudioCuePlayer> _audio = new();
    private readonly CaptureSink _sink = new();
    private readonly NavigationManager _navigationManager;
    private readonly QuickNavHandler _quickNav;
    private readonly Mock<IBrowseDocumentActions> _actions = new();
    private readonly Mock<IElementsListPresenter> _presenter = new();
    private VoxSettings _settings = new();
    private readonly BrowseModeController _controller;

    public BrowseModeControllerTests()
    {
        _engine
            .Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Callback<Utterance, CancellationToken>((u, _) => { lock (_spoken) _spoken.Add(u); })
            .Returns(Task.CompletedTask);
        _speechQueue = new SpeechQueue(_engine.Object, NullLogger<SpeechQueue>.Instance);

        _navigationManager = new NavigationManager(_sink, NullLogger<NavigationManager>.Instance);
        _quickNav = new QuickNavHandler(_audio.Object);

        var settingsMonitor = new Mock<IOptionsMonitor<VoxSettings>>();
        settingsMonitor.SetupGet(m => m.CurrentValue).Returns(() => _settings);

        _controller = new BrowseModeController(
            _speechQueue,
            _audio.Object,
            _navigationManager,
            _quickNav,
            new SayAllController(_speechQueue, NullLogger<SayAllController>.Instance),
            new AnnouncementBuilder(),
            new TypingEchoHandler(_sink, () => TypingEchoMode.Characters, NullLogger<TypingEchoHandler>.Instance),
            settingsMonitor.Object,
            _sink,
            _actions.Object,
            _presenter.Object,
            NullLogger<BrowseModeController>.Instance);
    }

    public void Dispose() => _speechQueue.Dispose();

    // Document: H1 "Welcome" / text "Intro text" / link "Read more" / edit "Search"
    private static VBufferDocument BuildDocument()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        root.AddChild(new MockElement { RuntimeId = [2], Name = "Welcome", AriaRole = "heading", AriaProperties = "level=1" });
        root.AddChild(new MockElement { RuntimeId = [3], Name = "Intro text" });
        var paragraph = new MockElement { RuntimeId = [6], ControlType = "Group" };
        paragraph.AddChild(new MockElement { RuntimeId = [4], Name = "Read more", ControlType = "Hyperlink" });
        root.AddChild(paragraph);
        root.AddChild(new MockElement { RuntimeId = [5], Name = "Search", ControlType = "Edit" });
        return new VBufferBuilder().Build(root);
    }

    private VBufferDocument LoadDocument(int[]? focusedId = null)
    {
        var doc = BuildDocument();
        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, doc, focusedId));
        return doc;
    }

    private async Task<Utterance> WaitForSpeech(Func<Utterance, bool> match)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            lock (_spoken)
            {
                var found = _spoken.FirstOrDefault(match);
                if (found is not null) return found;
            }
            await Task.Delay(10);
        }
        lock (_spoken)
            throw new Xunit.Sdk.XunitException("Expected speech not found. Spoken: " + string.Join(" | ", _spoken.Select(u => u.Text)));
    }

    // -------------------------------------------------------------------------
    // Document lifecycle
    // -------------------------------------------------------------------------

    [Fact]
    public void DocumentChanged_ActivatesAndDeactivates()
    {
        var changes = new List<bool>();
        _controller.DocumentActiveChanged += (_, active) => changes.Add(active);

        LoadDocument();
        Assert.True(_controller.IsDocumentActive);

        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, null));
        Assert.False(_controller.IsDocumentActive);
        Assert.Null(_quickNav.CurrentDocument);
        Assert.Equal([true, false], changes);
    }

    [Fact]
    public void DocumentChanged_WithFocusedElement_PositionsCursorThere()
    {
        var doc = LoadDocument(focusedId: [4]);

        Assert.Same(doc.FindByRuntimeId([4]), _quickNav.CurrentNode);
        Assert.Equal(doc.FindByRuntimeId([4])!.TextRange.Start, _controller.Cursor!.TextOffset);
    }

    // -------------------------------------------------------------------------
    // Navigation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task QuickNav_AnnouncesWithInterruptPriority_AndMovesCursor()
    {
        var doc = LoadDocument();

        _controller.HandleCommand(NavigationCommand.NextLink);

        var utterance = await WaitForSpeech(u => u.Text.Contains("Read more"));
        Assert.Equal(SpeechPriority.Interrupt, utterance.Priority);
        Assert.Equal(doc.FindByRuntimeId([4])!.TextRange.Start, _controller.Cursor!.TextOffset);
    }

    [Fact]
    public async Task QuickNav_UsesVerbosityFromSettings()
    {
        _settings = new VoxSettings { VerbosityLevel = VerbosityLevel.Advanced };
        LoadDocument();

        _controller.HandleCommand(NavigationCommand.NextHeading);

        var utterance = await WaitForSpeech(u => u.Text.Contains("Welcome"));
        Assert.DoesNotContain("heading level", utterance.Text);
    }

    [Fact]
    public async Task NextLine_AfterQuickNav_ReadsFollowingLine()
    {
        LoadDocument();
        _controller.HandleCommand(NavigationCommand.NextHeading); // Welcome

        _controller.HandleCommand(NavigationCommand.NextLine);

        await WaitForSpeech(u => u.Text == "Intro text");
        Assert.Equal([3], _quickNav.CurrentNode!.UIARuntimeId);
    }

    [Fact]
    public async Task ReadCurrentLine_SpeaksLineAtCursor()
    {
        LoadDocument(focusedId: [3]);

        _controller.HandleCommand(NavigationCommand.ReadCurrentLine);

        await WaitForSpeech(u => u.Text == "Intro text");
    }

    [Fact]
    public void BrowseCommand_InFocusMode_IsIgnored()
    {
        LoadDocument();
        _navigationManager.SwitchTo(InteractionMode.Focus);

        _controller.HandleCommand(NavigationCommand.NextLink);

        Assert.Null(_quickNav.CurrentNode);
    }

    [Fact]
    public void FocusChanged_MovesVirtualCursorToFocusedElement()
    {
        var doc = LoadDocument();

        _controller.HandleFocusChanged(new FocusChangedEvent(
            DateTimeOffset.UtcNow, "Read more", "Hyperlink", RuntimeId: [4]));

        Assert.Same(doc.FindByRuntimeId([4]), _quickNav.CurrentNode);
    }

    [Fact]
    public void StopSpeech_CancelsSpeech()
    {
        _controller.HandleCommand(NavigationCommand.StopSpeech);

        _engine.Verify(e => e.Cancel(), Times.AtLeastOnce);
    }

    // -------------------------------------------------------------------------
    // Activation and Elements List
    // -------------------------------------------------------------------------

    [Fact]
    public void ActivateElement_ActivatesCurrentNode()
    {
        LoadDocument(focusedId: [4]);
        _actions.Setup(a => a.ActivateAsync(It.IsAny<VBufferNode>())).ReturnsAsync(true);

        _controller.HandleCommand(NavigationCommand.ActivateElement);

        _actions.Verify(a => a.ActivateAsync(It.Is<VBufferNode>(n => n.Name == "Read more")), Times.Once);
        Assert.Equal(InteractionMode.Browse, _navigationManager.CurrentMode);
    }

    [Fact]
    public void ActivateElement_OnEditField_SwitchesToFocusMode()
    {
        LoadDocument(focusedId: [5]);
        _actions.Setup(a => a.ActivateAsync(It.IsAny<VBufferNode>())).ReturnsAsync(true);

        _controller.HandleCommand(NavigationCommand.ActivateElement);

        Assert.Equal(InteractionMode.Focus, _navigationManager.CurrentMode);
        _actions.Verify(a => a.ActivateAsync(It.Is<VBufferNode>(n => n.Name == "Search")), Times.Once);
    }

    [Fact]
    public async Task ElementsList_DeactivatesBrowseKeysWhileOpen_AndJumpsToSelection()
    {
        var doc = LoadDocument();
        var selection = new TaskCompletionSource<VBufferNode?>();
        _presenter.Setup(p => p.ShowAsync(It.IsAny<VBufferDocument>(), It.IsAny<VBufferNode?>())).Returns(selection.Task);

        _controller.HandleCommand(NavigationCommand.ElementsList);
        Assert.False(_controller.IsDocumentActive);

        selection.SetResult(doc.FindByRuntimeId([2]));
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (_sink.OfType<ElementsListClosedEvent>().Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        var closed = Assert.Single(_sink.OfType<ElementsListClosedEvent>());

        _controller.HandleElementsListClosed(closed);

        Assert.True(_controller.IsDocumentActive);
        Assert.Same(doc.FindByRuntimeId([2]), _quickNav.CurrentNode);
        await WaitForSpeech(u => u.Text.Contains("Welcome"));
    }

    // -------------------------------------------------------------------------
    // Typing echo and incremental updates
    // -------------------------------------------------------------------------

    private static RawKeyEvent KeyUp(int vk) =>
        new(DateTimeOffset.UtcNow, new KeyEvent { VkCode = vk, IsKeyDown = false });

    [Fact]
    public void RawKey_InBrowseModeOverDocument_IsNotEchoed()
    {
        LoadDocument();

        _controller.HandleRawKey(KeyUp(0x41));

        Assert.Empty(_sink.OfType<TypingEchoEvent>());
    }

    [Fact]
    public void RawKey_WithoutDocument_IsEchoed()
    {
        _controller.HandleRawKey(KeyUp(0x41));

        Assert.Single(_sink.OfType<TypingEchoEvent>());
    }

    [Fact]
    public void RawKey_AfterFocusLeavesTheDocumentForANativeEdit_IsEchoed()
    {
        LoadDocument();
        // Ctrl+L: focus moves to the browser's address bar, which isn't in the buffer
        _controller.HandleFocusChanged(new FocusChangedEvent(
            DateTimeOffset.UtcNow, "Address and search bar", "Edit", RuntimeId: [42, 1]));

        _controller.HandleRawKey(KeyUp(0x41));

        Assert.Single(_sink.OfType<TypingEchoEvent>());
    }

    [Fact]
    public void SubtreeChanged_KeepsCurrentElement()
    {
        LoadDocument(focusedId: [4]);
        var newIntro = new MockElement { RuntimeId = [3], Name = "A much longer introduction" };

        _controller.HandleSubtreeChanged(new SubtreeChangedEvent(DateTimeOffset.UtcNow, [3], newIntro));

        var doc = _quickNav.CurrentDocument!;
        Assert.Contains("A much longer introduction", doc.FlatText);
        Assert.Same(doc.FindByRuntimeId([4]), _quickNav.CurrentNode);
        Assert.Equal(doc.FindByRuntimeId([4])!.TextRange.Start, _controller.Cursor!.TextOffset);
    }

    // -------------------------------------------------------------------------
    // Round 2 fixes
    // -------------------------------------------------------------------------

    [Fact]
    public void PasswordField_EchoesStarsInsteadOfCharacters()
    {
        _controller.HandleFocusChanged(new FocusChangedEvent(
            DateTimeOffset.UtcNow, "Password", "Edit", RuntimeId: [9], IsPassword: true));

        _controller.HandleRawKey(KeyUp(0x41));

        var echo = Assert.Single(_sink.OfType<TypingEchoEvent>());
        Assert.Equal("star", echo.Text);
    }

    [Fact]
    public async Task ElementsList_FocusEventsDuringAndAfterDialog_DoNotUndoJump()
    {
        var doc = LoadDocument(focusedId: [4]);
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Read more", "Hyperlink", RuntimeId: [4]));
        var selection = new TaskCompletionSource<VBufferNode?>();
        _presenter.Setup(p => p.ShowAsync(It.IsAny<VBufferDocument>(), It.IsAny<VBufferNode?>())).Returns(selection.Task);

        _controller.HandleCommand(NavigationCommand.ElementsList);

        // Focus moves into the dialog's list...
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Elements", "List", RuntimeId: [99, 1]));
        selection.SetResult(doc.FindByRuntimeId([2]));
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (_sink.OfType<ElementsListClosedEvent>().Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        _controller.HandleElementsListClosed(_sink.OfType<ElementsListClosedEvent>()[0]);

        // ...then returns to the link that was focused before the dialog opened
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Read more", "Hyperlink", RuntimeId: [4]));

        Assert.Same(doc.FindByRuntimeId([2]), _quickNav.CurrentNode);
        Assert.Equal(InteractionMode.Browse, _navigationManager.CurrentMode);
    }

    [Fact]
    public async Task SayAll_FinishedEarlier_DoesNotPullCursorBackAfterFocusMove()
    {
        var doc = LoadDocument();
        _controller.HandleCommand(NavigationCommand.SayAll);

        // Let Say All read to the end
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!_spoken.Any(u => u.Text.Contains("Search")) && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        await Task.Delay(100);

        // User moves focus to the link, then navigates to the next form field from there
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Read more", "Hyperlink", RuntimeId: [4]));
        _controller.HandleCommand(NavigationCommand.ReadCurrentLine);

        await WaitForSpeech(u => u.Text == "Read more");
        Assert.Same(doc.FindByRuntimeId([4]), _quickNav.CurrentNode);
    }

    [Theory]
    [InlineData(NavigationCommand.SayAll)]
    [InlineData(NavigationCommand.ElementsList)]
    [InlineData(NavigationCommand.ToggleMode)]
    public async Task DocumentCommand_WithoutDocument_SaysNotInADocument(NavigationCommand command)
    {
        _controller.HandleCommand(command);

        await WaitForSpeech(u => u.Text == "Not in a document");
        Assert.Equal(InteractionMode.Browse, _navigationManager.CurrentMode);
    }

    [Fact]
    public async Task ReadCommand_WithoutDocumentOrText_SaysNoText()
    {
        _controller.HandleCommand(NavigationCommand.ReadCurrentLine);

        await WaitForSpeech(u => u.Text == "No text");
    }

    private sealed class FakeTextReader : IFocusedTextReader
    {
        public bool HasFocusedText { get; set; } = true;
        public List<TextReadKind> Reads { get; } = new();
        public void Read(TextReadKind kind) => Reads.Add(kind);
        public int SayAllSources { get; private set; }
        public ISayAllSource CreateSayAllSource()
        {
            SayAllSources++;
            return new TextDocumentSayAllSource(() => new Vox.Core.Text.StringTextDocument("one"), f => Task.FromResult(f()));
        }
    }

    private BrowseModeController ControllerWith(IFocusedTextReader reader)
    {
        var settingsMonitor = new Mock<IOptionsMonitor<VoxSettings>>();
        settingsMonitor.SetupGet(m => m.CurrentValue).Returns(() => _settings);
        return new BrowseModeController(_speechQueue, _audio.Object, _navigationManager, _quickNav,
            new SayAllController(_speechQueue, NullLogger<SayAllController>.Instance), new AnnouncementBuilder(),
            new TypingEchoHandler(_sink, () => TypingEchoMode.Characters, NullLogger<TypingEchoHandler>.Instance),
            settingsMonitor.Object, _sink, _actions.Object, _presenter.Object,
            NullLogger<BrowseModeController>.Instance, reader);
    }

    [Theory]
    [InlineData(NavigationCommand.ReadCurrentLine, TextReadKind.Line)]
    [InlineData(NavigationCommand.ReadCurrentWord, TextReadKind.Word)]
    [InlineData(NavigationCommand.ReadCurrentChar, TextReadKind.Character)]
    [InlineData(NavigationCommand.ReadSelection, TextReadKind.Selection)]
    public void ReadCommand_OutsideADocument_ReadsTheFocusedTextControl(NavigationCommand command, TextReadKind kind)
    {
        var reader = new FakeTextReader();
        var controller = ControllerWith(reader);

        controller.HandleCommand(command);

        Assert.Equal([kind], reader.Reads);
    }

    [Fact]
    public void SayAll_OutsideADocument_ReadsTheFocusedTextControl()
    {
        var reader = new FakeTextReader();
        var controller = ControllerWith(reader);

        controller.HandleCommand(NavigationCommand.SayAll);

        Assert.Equal(1, reader.SayAllSources);
    }

    [Fact]
    public void ReadCommand_InBrowseMode_ReadsTheBufferNotTheFocusedControl()
    {
        var reader = new FakeTextReader();
        var controller = ControllerWith(reader);
        controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, BuildDocument(), null));

        controller.HandleCommand(NavigationCommand.ReadCurrentLine);

        Assert.Empty(reader.Reads);
    }

    [Fact]
    public async Task ReadCurrentChar_InBrowseMode_SpeaksTheCharacterAtTheCursor()
    {
        LoadDocument();

        _controller.HandleCommand(NavigationCommand.ReadCurrentChar);

        await WaitForSpeech(u => u.Text == "W"); // "Welcome"
    }

    [Fact]
    public void FocusOutsideDocument_DoesNotSwitchMode()
    {
        LoadDocument();
        _navigationManager.SwitchTo(InteractionMode.Focus);

        // Focus moves to another application's control (not in the buffer)
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Notepad", "Document", RuntimeId: [500]));

        Assert.Equal(InteractionMode.Focus, _navigationManager.CurrentMode);
    }

    [Fact]
    public void DocumentChanged_ResetsToBrowseModeSilently()
    {
        LoadDocument();
        _navigationManager.SwitchTo(InteractionMode.Focus);
        var posted = _sink.OfType<ModeChangedEvent>().Count;

        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, null));

        Assert.Equal(InteractionMode.Browse, _navigationManager.CurrentMode);
        Assert.Equal(posted, _sink.OfType<ModeChangedEvent>().Count);
    }

    [Fact]
    public async Task PropertyChanged_OnFocusedElement_AnnouncesExpandedAndValue()
    {
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Menu", "Button", RuntimeId: [7]));
        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [7], 30070, 1));
        await WaitForSpeech(u => u.Text == "expanded");

        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Size", "ComboBox", RuntimeId: [8]));
        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [8], 30045, "Large"));
        await WaitForSpeech(u => u.Text == "Large");
    }

    [Fact]
    public async Task ElementSelected_InADesktopListWithoutFocusMoving_IsAnnounced()
    {
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Files", "List", RuntimeId: [7]));

        _controller.HandleElementSelected(new ElementSelectedEvent(DateTimeOffset.UtcNow, [7, 3], "report.docx"));

        await WaitForSpeech(u => u.Text == "report.docx");
    }

    [Fact]
    public async Task PropertyChanged_SliderRangeValue_IsAnnounced()
    {
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Volume", "Slider", RuntimeId: [7]));

        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [7], 30047, 42.0));

        await WaitForSpeech(u => u.Text == "42");
    }

    [Fact]
    public async Task PropertyChanged_ProgressBarRangeValue_IsLeftToTheProgressReporter()
    {
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Copying", "ProgressBar", RuntimeId: [7]));

        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [7], 30047, 42.0));

        await Task.Delay(100);
        lock (_spoken) Assert.DoesNotContain(_spoken, u => u.Text == "42");
    }

    [Fact]
    public async Task PropertyChanged_IsEnabled_SaysAvailableOrUnavailable()
    {
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Save", "Button", RuntimeId: [7]));

        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [7], 30010, false));
        await WaitForSpeech(u => u.Text == "unavailable");
        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [7], 30010, true));
        await WaitForSpeech(u => u.Text == "available");
    }

    [Fact]
    public async Task PropertyChanged_SameChangeFromTwoSubscriptions_IsSpokenOnce()
    {
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Remember me", "CheckBox", RuntimeId: [7], ToggleState: 0));

        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [7], 30086, 1));
        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [7], 30086, 1));
        await WaitForSpeech(u => u.Text == "checked");
        await Task.Delay(100);

        lock (_spoken) Assert.Single(_spoken, u => u.Text == "checked");
    }

    [Fact]
    public async Task PropertyChanged_ValueOfEditOrOtherElement_IsNotAnnounced()
    {
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Name", "Edit", RuntimeId: [7]));
        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [7], 30045, "typed text"));
        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [8], 30070, 1));

        await Task.Delay(200);
        lock (_spoken)
        {
            Assert.DoesNotContain(_spoken, u => u.Text == "typed text");
            Assert.DoesNotContain(_spoken, u => u.Text == "expanded");
        }
    }

    [Fact]
    public async Task ElementSelected_WithoutFocusChange_IsAnnounced()
    {
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Fruit", "List", RuntimeId: [7]));

        _controller.HandleElementSelected(new ElementSelectedEvent(DateTimeOffset.UtcNow, [7, 2], "Banana"));

        await WaitForSpeech(u => u.Text == "Banana");
    }

    [Fact]
    public void SubtreeChanged_UnknownElement_RequestsFullRecapture()
    {
        LoadDocument();

        _controller.HandleSubtreeChanged(new SubtreeChangedEvent(
            DateTimeOffset.UtcNow, [777], new MockElement { RuntimeId = [777], Name = "New" }));

        _actions.Verify(a => a.RequestRecapture(null), Times.Once);
    }

    [Fact]
    public void SubtreeChanged_TextAppearsUnderNamedAncestor_RequestsAncestorRecapture()
    {
        // Button "Submit" with an empty child: the button emits its own name
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        var button = new MockElement { RuntimeId = [2], Name = "Submit", ControlType = "Button" };
        button.AddChild(new MockElement { RuntimeId = [3], Name = "" });
        root.AddChild(button);
        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, new VBufferBuilder().Build(root)));

        // The child gains text: the button must no longer emit its own name
        _controller.HandleSubtreeChanged(new SubtreeChangedEvent(
            DateTimeOffset.UtcNow, [3], new MockElement { RuntimeId = [3], Name = "Send" }));

        _actions.Verify(a => a.RequestRecapture(It.Is<int[]>(id => id.SequenceEqual(new[] { 2 }))), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Round 3 fixes
    // -------------------------------------------------------------------------

    private void LoadCustom(MockElement root, int[]? focusedId = null) =>
        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, new VBufferBuilder().Build(root), focusedId));

    [Fact]
    public void FocusMovingToEditField_EntersFocusMode_ButNotForButtons()
    {
        LoadDocument();

        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Read more", "Hyperlink", RuntimeId: [4]));
        Assert.Equal(InteractionMode.Browse, _navigationManager.CurrentMode);

        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Search", "Edit", RuntimeId: [5]));
        Assert.Equal(InteractionMode.Focus, _navigationManager.CurrentMode);
    }

    [Fact]
    public void LoadTimeFocusOnEditField_StaysInBrowseMode()
    {
        LoadDocument(focusedId: [5]);

        Assert.Equal(InteractionMode.Browse, _navigationManager.CurrentMode);
        Assert.Equal([5], _quickNav.CurrentNode!.UIARuntimeId);
    }

    [Fact]
    public void EscapeTarget_FollowsExpandedComboBoxAndMenus()
    {
        var values = new List<bool>();
        _controller.EscapeGoesToPageChanged += (_, v) => values.Add(v);

        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Size", "ComboBox", RuntimeId: [8], IsExpandable: true, IsExpanded: false));
        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [8], 30070, 1));   // expanded
        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [8], 30070, 0));   // collapsed
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Open", "MenuItem", RuntimeId: [9]));

        Assert.Equal([true, false, true], values);
    }

    [Fact]
    public void SubtreeChanged_FromAnotherDocument_IsIgnored()
    {
        LoadDocument();
        var before = _quickNav.CurrentDocument;

        _controller.HandleSubtreeChanged(new SubtreeChangedEvent(
            DateTimeOffset.UtcNow, [3], new MockElement { RuntimeId = [3], Name = "Other page" }, DocumentRuntimeId: [42]));

        Assert.Same(before, _quickNav.CurrentDocument);
        _actions.Verify(a => a.RequestRecapture(It.IsAny<int[]?>()), Times.Never);
    }

    [Fact]
    public void SubtreeChanged_UnknownElement_RequestsFullRecaptureOnlyOnce()
    {
        LoadDocument();
        var unknown = new MockElement { RuntimeId = [777], Name = "New" };

        _controller.HandleSubtreeChanged(new SubtreeChangedEvent(DateTimeOffset.UtcNow, [777], unknown, DocumentRuntimeId: [1]));
        _controller.HandleSubtreeChanged(new SubtreeChangedEvent(DateTimeOffset.UtcNow, [777], unknown, DocumentRuntimeId: [1]));

        _actions.Verify(a => a.RequestRecapture(null), Times.Once);
    }

    [Fact]
    public void SubtreeChanged_UnknownElementWithKnownAncestor_RecapturesThatAncestor()
    {
        LoadDocument();

        _controller.HandleSubtreeChanged(new SubtreeChangedEvent(
            DateTimeOffset.UtcNow, [777], new MockElement { RuntimeId = [777], Name = "New" },
            DocumentRuntimeId: [1], AncestorRuntimeIds: [[778], [6], [1]]));

        _actions.Verify(a => a.RequestRecapture(It.Is<int[]>(id => id.SequenceEqual(new[] { 6 }))), Times.Once);
        _actions.Verify(a => a.RequestRecapture(null), Times.Never);
    }

    [Fact]
    public void SubtreeChanged_EarlierTextGrows_CursorStaysOnSameCharacter()
    {
        var doc = LoadDocument();
        var intro = doc.FindByRuntimeId([3])!;
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Intro", "Text", RuntimeId: [3]));
        _controller.Cursor!.MoveTo(intro.TextRange.Start + 5);
        _controller.HandleCommand(NavigationCommand.NextChar); // updates CurrentNode from the cursor
        var charBefore = _controller.Cursor.CurrentChar;
        int withinBefore = _controller.Cursor.TextOffset - intro.TextRange.Start;

        // The heading before it gets 10 characters longer
        _controller.HandleSubtreeChanged(new SubtreeChangedEvent(DateTimeOffset.UtcNow, [2],
            new MockElement { RuntimeId = [2], Name = "Welcome1234567890", AriaRole = "heading", AriaProperties = "level=1" }));

        var newIntro = _quickNav.CurrentDocument!.FindByRuntimeId([3])!;
        Assert.Equal(charBefore, _controller.Cursor.CurrentChar);
        Assert.Equal(withinBefore, _controller.Cursor.TextOffset - newIntro.TextRange.Start);
    }

    [Fact]
    public void SubtreeChanged_CurrentNodeReplacedByLongerVersion_KeepsOffsetWithinIt()
    {
        var doc = LoadDocument(focusedId: [3]);
        int start = doc.FindByRuntimeId([3])!.TextRange.Start;
        _controller.Cursor!.MoveTo(start + 6);

        _controller.HandleSubtreeChanged(new SubtreeChangedEvent(DateTimeOffset.UtcNow, [3],
            new MockElement { RuntimeId = [3], Name = "Intro text, now much longer" }));

        var newStart = _quickNav.CurrentDocument!.FindByRuntimeId([3])!.TextRange.Start;
        Assert.Equal(newStart + 6, _controller.Cursor.TextOffset);
    }

    [Fact]
    public void ReturningToADocument_RestoresTheReadingPosition()
    {
        var docA = LoadDocument();
        _controller.HandleCommand(NavigationCommand.NextLink);
        int offsetInA = _controller.Cursor!.TextOffset;

        var rootB = new MockElement { RuntimeId = [50], ControlType = "Document" };
        rootB.AddChild(new MockElement { RuntimeId = [51], Name = "Other page" });
        LoadCustom(rootB);

        // Back to A with only the page itself focused
        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, BuildDocument(), [1]));

        Assert.Equal(offsetInA, _controller.Cursor!.TextOffset);
        Assert.Equal([4], _quickNav.CurrentNode!.UIARuntimeId);
    }

    [Fact]
    public async Task FocusReturningAfterElementsListJump_IsNotAnnounced()
    {
        var doc = LoadDocument(focusedId: [4]);
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Read more", "Hyperlink", RuntimeId: [4]));
        var selection = new TaskCompletionSource<VBufferNode?>();
        _presenter.Setup(p => p.ShowAsync(It.IsAny<VBufferDocument>(), It.IsAny<VBufferNode?>())).Returns(selection.Task);
        _controller.HandleCommand(NavigationCommand.ElementsList);
        selection.SetResult(doc.FindByRuntimeId([2]));
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (_sink.OfType<ElementsListClosedEvent>().Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        _controller.HandleElementsListClosed(_sink.OfType<ElementsListClosedEvent>()[0]);

        var returning = new FocusChangedEvent(DateTimeOffset.UtcNow, "Read more", "Hyperlink", RuntimeId: [4]);
        _controller.HandleFocusChanged(returning);

        Assert.False(_controller.ShouldAnnounceFocus(returning));
        var later = new FocusChangedEvent(DateTimeOffset.UtcNow, "Search", "Edit", RuntimeId: [5]);
        _controller.HandleFocusChanged(later);
        Assert.True(_controller.ShouldAnnounceFocus(later));
    }

    [Fact]
    public async Task ElementsListSelection_NoLongerOnPage_IsReported()
    {
        LoadDocument();
        var selection = new TaskCompletionSource<VBufferNode?>();
        _presenter.Setup(p => p.ShowAsync(It.IsAny<VBufferDocument>(), It.IsAny<VBufferNode?>())).Returns(selection.Task);
        _controller.HandleCommand(NavigationCommand.ElementsList);
        selection.SetResult(new VBufferNode { UIARuntimeId = [999], Name = "Gone" });
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (_sink.OfType<ElementsListClosedEvent>().Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        _controller.HandleElementsListClosed(_sink.OfType<ElementsListClosedEvent>()[0]);

        await WaitForSpeech(u => u.Text == "Element no longer on page");
        _audio.Verify(a => a.Play("error"), Times.Once);
    }

    [Fact]
    public void ActivateElement_OnLinkText_ActivatesTheLink()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        var link = new MockElement { RuntimeId = [2], Name = "Docs", ControlType = "Hyperlink" };
        link.AddChild(new MockElement { RuntimeId = [3], Name = "Docs" });
        root.AddChild(link);
        LoadCustom(root);
        _actions.Setup(a => a.ActivateAsync(It.IsAny<VBufferNode>())).ReturnsAsync(true);
        _controller.HandleCommand(NavigationCommand.NextChar);
        _controller.HandleCommand(NavigationCommand.PrevChar); // cursor on the link's text node
        Assert.Equal([3], _quickNav.CurrentNode!.UIARuntimeId);

        _controller.HandleCommand(NavigationCommand.ActivateElement);

        _actions.Verify(a => a.ActivateAsync(It.Is<VBufferNode>(n => n.ControlType == "Hyperlink")), Times.Once);
    }

    [Fact]
    public async Task ToggleStateChange_OnFocusedCheckbox_IsAnnounced()
    {
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Subscribe", "CheckBox", RuntimeId: [7], ToggleState: 0));

        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [7], 30086, 1));

        await WaitForSpeech(u => u.Text == "checked");
    }

    [Fact]
    public async Task LineMoveOntoLink_SaysItIsALink()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        root.AddChild(new MockElement { RuntimeId = [2], Name = "First line" });
        var group = new MockElement { RuntimeId = [3], ControlType = "Group" };
        var link = new MockElement { RuntimeId = [4], Name = "Docs", ControlType = "Hyperlink" };
        link.AddChild(new MockElement { RuntimeId = [5], Name = "Docs" });
        group.AddChild(link);
        root.AddChild(group);
        LoadCustom(root);

        _controller.HandleCommand(NavigationCommand.NextLine);

        await WaitForSpeech(u => u.Text == "Docs, link");
    }

    // -------------------------------------------------------------------------
    // Round 4 fixes
    // -------------------------------------------------------------------------

    [Fact]
    public void AutomaticFocusMode_PlaysCueOnly()
    {
        LoadDocument();

        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Search", "Edit", RuntimeId: [5]));

        var modeChange = Assert.Single(_sink.OfType<ModeChangedEvent>());
        Assert.Equal(InteractionMode.Focus, modeChange.NewMode);
        Assert.False(modeChange.Announce);
    }

    [Fact]
    public void RepeatedFocusOnSameField_DoesNotUndoEscape()
    {
        LoadDocument();
        var focusOnEdit = new FocusChangedEvent(DateTimeOffset.UtcNow, "Search", "Edit", RuntimeId: [5]);
        _controller.HandleFocusChanged(focusOnEdit);
        Assert.Equal(InteractionMode.Focus, _navigationManager.CurrentMode);

        _controller.HandleCommand(NavigationCommand.ExitFocusMode); // Escape
        Assert.Equal(InteractionMode.Browse, _navigationManager.CurrentMode);

        // Chromium repeats the focus event for the same element
        _controller.HandleFocusChanged(focusOnEdit with { Timestamp = DateTimeOffset.UtcNow });

        Assert.Equal(InteractionMode.Browse, _navigationManager.CurrentMode);
    }

    [Fact]
    public void RepeatedFocusOnSameElement_DoesNotPullTheCursorBack()
    {
        var doc = LoadDocument();
        var focusOnLink = new FocusChangedEvent(DateTimeOffset.UtcNow, "Read more", "Hyperlink", RuntimeId: [4]);
        _controller.HandleFocusChanged(focusOnLink);
        Assert.Equal(doc.FindByRuntimeId([4])!.TextRange.Start, _controller.Cursor!.TextOffset);

        // The user reads on from the link, then Chromium repeats the focus event for it
        _controller.HandleCommand(NavigationCommand.NextLine);
        int readingAt = _controller.Cursor.TextOffset;
        Assert.NotEqual(doc.FindByRuntimeId([4])!.TextRange.Start, readingAt);
        _controller.HandleFocusChanged(focusOnLink with { Timestamp = DateTimeOffset.UtcNow });

        Assert.Equal(readingAt, _controller.Cursor.TextOffset);
    }

    [Fact]
    public void FocusReturningAfterLeavingTheElement_MovesTheCursorAgain()
    {
        var doc = LoadDocument();
        var focusOnLink = new FocusChangedEvent(DateTimeOffset.UtcNow, "Read more", "Hyperlink", RuntimeId: [4]);
        _controller.HandleFocusChanged(focusOnLink);
        _controller.HandleCommand(NavigationCommand.NextLine);

        // Focus goes to the page itself, then back to the link
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "", "Document", RuntimeId: [1]));
        _controller.HandleFocusChanged(focusOnLink with { Timestamp = DateTimeOffset.UtcNow });

        Assert.Equal(doc.FindByRuntimeId([4])!.TextRange.Start, _controller.Cursor!.TextOffset);
    }

    [Fact]
    public void ReturningToPageWithFocusInEditField_ResumesFocusMode()
    {
        LoadDocument();
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Search", "Edit", RuntimeId: [5]));
        Assert.Equal(InteractionMode.Focus, _navigationManager.CurrentMode);

        // Alt+Tab away: focus goes to another app, the document is unloaded
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Mail", "Window", RuntimeId: [900]));
        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, null));

        // Back: focus returns to the edit field before the document is re-captured
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Search", "Edit", RuntimeId: [5]));
        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, BuildDocument(), [5]));

        Assert.Equal(InteractionMode.Focus, _navigationManager.CurrentMode);
    }

    [Fact]
    public void FreshLoadWithAutofocusedField_StaysInBrowseMode()
    {
        LoadDocument(focusedId: [5]); // never visited before

        Assert.Equal(InteractionMode.Browse, _navigationManager.CurrentMode);
    }

    [Fact]
    public async Task StateChangeAfterFocus_IsQueuedNotInterrupting_AndRedundantOnesSkipped()
    {
        // Arrowing onto an unselected item, which then becomes selected
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Banana", "ListItem", RuntimeId: [7], IsSelected: false));
        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [7], 30079, true));
        var selected = await WaitForSpeech(u => u.Text == "selected");
        Assert.Equal(SpeechPriority.High, selected.Priority);

        // An item whose focus event already said "selected": the matching change adds nothing
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Cherry", "ListItem", RuntimeId: [8], IsSelected: true));
        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [8], 30079, true));
        await Task.Delay(150);

        lock (_spoken) Assert.Single(_spoken, u => u.Text == "selected");
    }

    [Fact]
    public void ReturningToChangedPage_RestoresPositionWithinTheSameElement()
    {
        var doc = LoadDocument(focusedId: [3]);
        int introStart = doc.FindByRuntimeId([3])!.TextRange.Start;
        _controller.Cursor!.MoveTo(introStart + 5);

        var rootB = new MockElement { RuntimeId = [50], ControlType = "Document" };
        rootB.AddChild(new MockElement { RuntimeId = [51], Name = "Other page" });
        LoadCustom(rootB);

        // Page A came back with a much longer heading before the intro text
        var rootA = new MockElement { RuntimeId = [1], ControlType = "Document" };
        rootA.AddChild(new MockElement { RuntimeId = [2], Name = "Welcome to the new and improved site", AriaRole = "heading", AriaProperties = "level=1" });
        rootA.AddChild(new MockElement { RuntimeId = [3], Name = "Intro text" });
        LoadCustom(rootA, focusedId: [1]);

        var newIntroStart = _quickNav.CurrentDocument!.FindByRuntimeId([3])!.TextRange.Start;
        Assert.Equal(newIntroStart + 5, _controller.Cursor!.TextOffset);
        Assert.Equal([3], _quickNav.CurrentNode!.UIARuntimeId);
    }

    [Fact]
    public async Task UnansweredFullRecapture_CanBeRequestedAgainAfterInterval()
    {
        _controller.FullRecaptureRetryInterval = TimeSpan.FromMilliseconds(100);
        LoadDocument();
        var unknown = new MockElement { RuntimeId = [777], Name = "New" };

        _controller.HandleSubtreeChanged(new SubtreeChangedEvent(DateTimeOffset.UtcNow, [777], unknown, DocumentRuntimeId: [1]));
        _controller.HandleSubtreeChanged(new SubtreeChangedEvent(DateTimeOffset.UtcNow, [777], unknown, DocumentRuntimeId: [1]));
        _actions.Verify(a => a.RequestRecapture(null), Times.Once);

        await Task.Delay(150); // the first re-capture never replied
        _controller.HandleSubtreeChanged(new SubtreeChangedEvent(DateTimeOffset.UtcNow, [777], unknown, DocumentRuntimeId: [1]));

        _actions.Verify(a => a.RequestRecapture(null), Times.Exactly(2));
    }

    [Theory]
    [InlineData(0xA0)] // Left Shift
    [InlineData(0x2D)] // Insert (screen reader key)
    public async Task BareModifierPress_DoesNotStopSayAll(int vk)
    {
        // Slow speech so Say All is still reading when the key arrives
        _engine
            .Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Returns((Utterance _, CancellationToken ct) => Task.Delay(2000, ct));
        LoadDocument();
        _controller.HandleCommand(NavigationCommand.SayAll);
        await Task.Delay(100);

        _controller.HandleRawKey(new RawKeyEvent(DateTimeOffset.UtcNow, new KeyEvent { VkCode = vk, IsKeyDown = true }));

        var sayAll = typeof(BrowseModeController)
            .GetField("_sayAllController", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(_controller) as SayAllController;
        Assert.True(sayAll!.IsReading);

        _controller.HandleRawKey(new RawKeyEvent(DateTimeOffset.UtcNow, new KeyEvent { VkCode = 0x41, IsKeyDown = true }));
        await Task.Delay(50);
        Assert.False(sayAll.IsReading);
    }

    // -------------------------------------------------------------------------
    // Round 5 fixes
    // -------------------------------------------------------------------------

    private const int UIA_NamePropertyId = 30005;
    private const int UIA_ValueValuePropertyId = 30045;
    private const int UIA_ToggleToggleStatePropertyId = 30086;

    private List<string> SpokenTexts()
    {
        lock (_spoken) return _spoken.Select(u => u.Text).ToList();
    }

    [Fact]
    public async Task EditableComboBoxValueChange_IsNotSpoken()
    {
        LoadDocument();
        var focus = new FocusChangedEvent(DateTimeOffset.UtcNow, "Search", "ComboBox", RuntimeId: [70], IsValueReadOnly: false);
        _controller.HandleFocusChanged(focus);

        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [70], UIA_ValueValuePropertyId, "new"));
        await Task.Delay(150);

        Assert.DoesNotContain("new", SpokenTexts());
    }

    [Fact]
    public async Task ReadOnlyComboBoxValueChange_IsSpoken()
    {
        LoadDocument();
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Fruit", "ComboBox", RuntimeId: [71], IsValueReadOnly: true));

        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [71], UIA_ValueValuePropertyId, "Banana"));

        await WaitForSpeech(u => u.Text == "Banana");
    }

    [Fact]
    public async Task OptionSelectedAndValueChange_SpeakTheOptionOnce()
    {
        LoadDocument();
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Fruit", "ComboBox", RuntimeId: [71]));

        _controller.HandleElementSelected(new ElementSelectedEvent(DateTimeOffset.UtcNow, [72], "Banana"));
        _controller.HandlePropertyChanged(new PropertyChangedEvent(DateTimeOffset.UtcNow, [71], UIA_ValueValuePropertyId, "Banana"));
        await WaitForSpeech(u => u.Text == "Banana");
        await Task.Delay(150);

        Assert.Single(SpokenTexts(), t => t == "Banana");
    }

    [Theory]
    [InlineData(UIA_ToggleToggleStatePropertyId, true)]
    [InlineData(30079, true)]  // SelectionItemIsSelected
    [InlineData(30070, true)]  // ExpandCollapseState
    [InlineData(UIA_NamePropertyId, true)]
    [InlineData(UIA_ValueValuePropertyId, true)]
    [InlineData(30010, false)] // IsEnabled
    public void ChangesBufferText_CoversStateNameAndValue(int propertyId, bool expected) =>
        Assert.Equal(expected, BrowseModeController.ChangesBufferText(propertyId));

    [Fact]
    public async Task ToggledCheckBox_ReadsNewStateAfterRecapture()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        root.AddChild(new MockElement { RuntimeId = [2], Name = "Intro" });
        root.AddChild(new MockElement { RuntimeId = [3], Name = "Subscribe", ControlType = "CheckBox", ToggleState = 0 });
        var doc = new VBufferBuilder().Build(root);
        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, doc));

        // The re-captured check box is now checked
        var updated = new MockElement { RuntimeId = [3], Name = "Subscribe", ControlType = "CheckBox", ToggleState = 1 };
        _controller.HandleSubtreeChanged(new SubtreeChangedEvent(DateTimeOffset.UtcNow, [3], updated, DocumentRuntimeId: [1]));

        _controller.HandleCommand(NavigationCommand.NextFormField);
        await WaitForSpeech(u => u.Text.Contains("Subscribe"));
        Assert.Contains(SpokenTexts(), t => t.Contains("Subscribe") && t.Contains("checked") && !t.Contains("not checked"));
    }

    [Fact]
    public void FocusLeavingDocument_DeactivatesBrowseKeysAtOnce_KeepingPosition()
    {
        var doc = LoadDocument(focusedId: [4]);
        var offset = _controller.Cursor!.TextOffset;

        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Address and search bar", "Edit", RuntimeId: [900]));

        Assert.False(_controller.IsDocumentActive);
        Assert.Same(doc, _quickNav.CurrentDocument);
        Assert.Equal(offset, _controller.Cursor!.TextOffset);
    }

    [Fact]
    public void FocusInDocumentReport_ReactivatesBrowseKeys()
    {
        LoadDocument();
        // A dialog added since the capture: not in the buffer, but in the same document
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "OK", "Button", RuntimeId: [901]));
        Assert.False(_controller.IsDocumentActive);

        _controller.HandleFocusInDocument(new FocusInDocumentEvent(DateTimeOffset.UtcNow, [1], [901]));

        Assert.True(_controller.IsDocumentActive);
    }

    [Fact]
    public void StaleFocusInDocumentReport_DoesNotReactivate()
    {
        LoadDocument();
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "OK", "Button", RuntimeId: [901]));
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Address", "Edit", RuntimeId: [900]));

        // The report about the earlier focus arrives after focus moved on
        _controller.HandleFocusInDocument(new FocusInDocumentEvent(DateTimeOffset.UtcNow, [1], [901]));

        Assert.False(_controller.IsDocumentActive);
    }

    [Fact]
    public void FocusReturningIntoDocument_Reactivates()
    {
        LoadDocument();
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Address", "Edit", RuntimeId: [900]));
        Assert.False(_controller.IsDocumentActive);

        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Read more", "Hyperlink", RuntimeId: [4]));

        Assert.True(_controller.IsDocumentActive);
    }

    private VBufferDocument LoadLongParagraphDocument()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        root.AddChild(new MockElement { RuntimeId = [2], Name = string.Join(" ", Enumerable.Repeat("word", 40)) }); // 199 chars
        root.AddChild(new MockElement { RuntimeId = [3], Name = "Next paragraph", AriaRole = "heading", AriaProperties = "level=2" });
        var doc = new VBufferBuilder().Build(root);
        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, doc));
        return doc;
    }

    [Fact]
    public async Task LongParagraph_IsReadInLinesOfTheConfiguredLength()
    {
        _settings = _settings with { MaxLineLength = 50 };
        LoadLongParagraphDocument();

        _controller.HandleCommand(NavigationCommand.ReadCurrentLine);

        var line = await WaitForSpeech(u => u.Text.StartsWith("word"));
        Assert.True(line.Text.Length <= 50, line.Text);
    }

    [Fact]
    public async Task NextParagraph_SkipsTheRestOfALongParagraph()
    {
        LoadLongParagraphDocument();

        _controller.HandleCommand(NavigationCommand.NextParagraph);

        await WaitForSpeech(u => u.Text.StartsWith("Next paragraph"));
    }

    [Fact]
    public async Task EndAndHome_MoveWithinTheLine()
    {
        LoadDocument(); // "Welcome" first

        _controller.HandleCommand(NavigationCommand.EndOfLine);
        await WaitForSpeech(u => u.Text == "e");
        Assert.Equal(6, _controller.Cursor!.TextOffset);

        _controller.HandleCommand(NavigationCommand.StartOfLine);
        await WaitForSpeech(u => u.Text.StartsWith("W, heading level 1"));
        Assert.Equal(0, _controller.Cursor!.TextOffset);
    }

    [Fact]
    public async Task BottomAndTopOfDocument()
    {
        LoadDocument();

        _controller.HandleCommand(NavigationCommand.BottomOfDocument);
        await WaitForSpeech(u => u.Text.Contains("Search"));

        _controller.HandleCommand(NavigationCommand.TopOfDocument);
        await WaitForSpeech(u => u.Text.Contains("Welcome"));
        Assert.Equal(0, _controller.Cursor!.TextOffset);
    }

    [Fact]
    public async Task NextTable_MovesToTheTableAndReadsItsFirstLine()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        root.AddChild(new MockElement { RuntimeId = [2], Name = "Intro" });
        var table = new MockElement { RuntimeId = [3], Name = "Prices", ControlType = "Table" };
        table.AddChild(new MockElement { RuntimeId = [4], Name = "Apples" });
        root.AddChild(table);
        var doc = new VBufferBuilder().Build(root);
        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, doc));

        _controller.HandleCommand(NavigationCommand.NextTable);

        await WaitForSpeech(u => u.Text == "Prices, table, Apples");
        Assert.Same(doc.FindByRuntimeId([3]), _quickNav.CurrentNode);
    }

    // -------------------------------------------------------------------------
    // Round 6 fixes
    // -------------------------------------------------------------------------

    [Fact]
    public void FocusMovingOntoAPageMenuItem_EntersFocusMode()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        root.AddChild(new MockElement { RuntimeId = [2], Name = "Actions", ControlType = "Button" });
        var menu = new MockElement { RuntimeId = [3], ControlType = "Menu", AriaRole = "menu" };
        menu.AddChild(new MockElement { RuntimeId = [4], Name = "Delete", ControlType = "MenuItem", AriaRole = "menuitem" });
        root.AddChild(menu);
        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, new VBufferBuilder().Build(root)));

        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Actions", "Button", RuntimeId: [2]));
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Delete", "MenuItem", AriaRole: "menuitem", RuntimeId: [4]));

        Assert.Equal(InteractionMode.Focus, _navigationManager.CurrentMode);
    }

    [Fact]
    public void FocusInDocumentReport_ForTheLatestFocusChange_ReactivatesEvenForAnotherElement()
    {
        LoadDocument();
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Dialog", "Group", RuntimeId: [901]));
        Assert.False(_controller.IsDocumentActive);

        // The tracker read focus itself and found a descendant of the event's element
        _controller.HandleFocusInDocument(new FocusInDocumentEvent(DateTimeOffset.UtcNow, [1], [902], _controller.FocusSequence));

        Assert.True(_controller.IsDocumentActive);
    }

    [Fact]
    public void FocusInDocumentReport_ForAnEarlierFocusChange_IsIgnored()
    {
        LoadDocument();
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "OK", "Button", RuntimeId: [901]));
        var earlier = _controller.FocusSequence;
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Address", "Edit", RuntimeId: [900]));

        _controller.HandleFocusInDocument(new FocusInDocumentEvent(DateTimeOffset.UtcNow, [1], [901], earlier));

        Assert.False(_controller.IsDocumentActive);
    }

    [Fact]
    public async Task Quit_NeedsASecondPress()
    {
        int quits = 0;
        _controller.QuitRequested += (_, _) => quits++;

        _controller.HandleCommand(NavigationCommand.Quit);
        await WaitForSpeech(u => u.Text == "Press Insert Q again to exit Vox");
        Assert.Equal(0, quits);

        _controller.HandleCommand(NavigationCommand.Quit);
        Assert.Equal(1, quits);
    }

    [Fact]
    public void RunSetup_RaisesSetupRequested()
    {
        int requests = 0;
        _controller.SetupRequested += (_, _) => requests++;

        _controller.HandleCommand(NavigationCommand.RunSetup);

        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task ElementsList_IsGivenTheCursorsElement()
    {
        var doc = LoadDocument(focusedId: [4]);
        VBufferNode? passed = null;
        _presenter.Setup(p => p.ShowAsync(It.IsAny<VBufferDocument>(), It.IsAny<VBufferNode?>()))
            .Callback((VBufferDocument _, VBufferNode? current) => passed = current)
            .Returns(Task.FromResult<VBufferNode?>(null));

        _controller.HandleCommand(NavigationCommand.ElementsList);
        await Task.Delay(50);

        Assert.Same(doc.FindByRuntimeId([4]), passed);
    }

    // -------------------------------------------------------------------------
    // Round 7 fixes
    // -------------------------------------------------------------------------

    // Document: main landmark [ navigation landmark [ link "Home" ], text "Article" ], footer landmark
    private VBufferDocument LoadNestedLandmarks()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        var main = new MockElement { RuntimeId = [2], ControlType = "Group", AriaRole = "main" };
        var nav = new MockElement { RuntimeId = [3], ControlType = "Group", AriaRole = "navigation" };
        nav.AddChild(new MockElement { RuntimeId = [4], Name = "Home", ControlType = "Hyperlink" });
        main.AddChild(nav);
        main.AddChild(new MockElement { RuntimeId = [5], Name = "Article" });
        root.AddChild(main);
        var footer = new MockElement { RuntimeId = [6], ControlType = "Group", AriaRole = "contentinfo" };
        footer.AddChild(new MockElement { RuntimeId = [7], Name = "Copyright" });
        root.AddChild(footer);
        var doc = new VBufferBuilder().Build(root);
        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, doc));
        return doc;
    }

    [Fact]
    public void NextLandmark_FromInsideMainAfterItsNavigation_GoesForward()
    {
        var doc = LoadNestedLandmarks();
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Article", "Text", RuntimeId: [5]));

        _controller.HandleCommand(NavigationCommand.NextLandmark);

        Assert.Same(doc.FindByRuntimeId([6]), _quickNav.CurrentNode); // the footer, not the navigation behind
    }

    [Fact]
    public void PrevLandmark_FromInsideMainAfterItsNavigation_FindsThatNavigation()
    {
        var doc = LoadNestedLandmarks();
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Article", "Text", RuntimeId: [5]));

        _controller.HandleCommand(NavigationCommand.PrevLandmark);

        Assert.Same(doc.FindByRuntimeId([3]), _quickNav.CurrentNode);
    }

    [Fact]
    public void PrevLandmark_FromInsideNavigation_SkipsTheLandmarksItIsIn()
    {
        var doc = LoadNestedLandmarks();
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Home", "Hyperlink", RuntimeId: [4]));

        _controller.HandleCommand(NavigationCommand.PrevLandmark);

        // Nothing before main or the navigation: wraps to the last landmark
        Assert.Same(doc.FindByRuntimeId([6]), _quickNav.CurrentNode);
    }

    // Document: menubar [ menuitem "Products" ], menu [ menuitem "Delete" ]
    private void LoadMenus()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        var bar = new MockElement { RuntimeId = [2], ControlType = "MenuBar", AriaRole = "menubar" };
        bar.AddChild(new MockElement { RuntimeId = [3], Name = "Products", ControlType = "MenuItem", AriaRole = "menuitem" });
        root.AddChild(bar);
        var menu = new MockElement { RuntimeId = [4], ControlType = "Menu", AriaRole = "menu" };
        menu.AddChild(new MockElement { RuntimeId = [5], Name = "Delete", ControlType = "MenuItem", AriaRole = "menuitem" });
        root.AddChild(menu);
        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, new VBufferBuilder().Build(root)));
    }

    private bool EscapeGoesToPageAfterFocus(FocusChangedEvent focus)
    {
        bool value = false;
        _controller.EscapeGoesToPageChanged += (_, v) => value = v;
        _controller.HandleFocusChanged(focus);
        return value;
    }

    [Fact]
    public void Escape_OnAClosedMenuBarItem_LeavesFocusMode()
    {
        LoadMenus();
        Assert.False(EscapeGoesToPageAfterFocus(
            new FocusChangedEvent(DateTimeOffset.UtcNow, "Products", "MenuItem", AriaRole: "menuitem", RuntimeId: [3])));
    }

    [Fact]
    public void Escape_OnAnItemInAPopupMenu_GoesToThePage()
    {
        LoadMenus();
        Assert.True(EscapeGoesToPageAfterFocus(
            new FocusChangedEvent(DateTimeOffset.UtcNow, "Delete", "MenuItem", AriaRole: "menuitem", RuntimeId: [5])));
    }

    [Fact]
    public void Escape_OnAnExpandedMenuBarItem_GoesToThePage()
    {
        LoadMenus();
        Assert.True(EscapeGoesToPageAfterFocus(
            new FocusChangedEvent(DateTimeOffset.UtcNow, "Products", "MenuItem", AriaRole: "menuitem", RuntimeId: [3],
                IsExpandable: true, IsExpanded: true)));
    }

    [Fact]
    public async Task EmptyTable_IsAnnouncedWithoutTheFollowingLine()
    {
        var root = new MockElement { RuntimeId = [1], ControlType = "Document" };
        root.AddChild(new MockElement { RuntimeId = [2], Name = "Intro" });
        root.AddChild(new MockElement { RuntimeId = [3], Name = "Prices", ControlType = "Table" });
        root.AddChild(new MockElement { RuntimeId = [4], Name = "After the table", AriaRole = "heading", AriaProperties = "level=2" });
        var doc = new VBufferBuilder().Build(root);
        _controller.HandleDocumentChanged(new DocumentChangedEvent(DateTimeOffset.UtcNow, doc));

        _controller.HandleCommand(NavigationCommand.NextTable);

        var spoken = await WaitForSpeech(u => u.Text.Contains("table"));
        Assert.DoesNotContain("After the table", spoken.Text);
    }

    // -------------------------------------------------------------------------
    // Round 8 fixes
    // -------------------------------------------------------------------------

    [Fact]
    public void FocusOnThePageItself_KeepsTheReadingPosition()
    {
        var doc = LoadDocument();
        _controller.HandleCommand(NavigationCommand.NextLink); // cursor on "Read more"
        var offset = _controller.Cursor!.TextOffset;
        var node = _quickNav.CurrentNode;
        Assert.True(offset > 0);

        // A dialog closed and focus fell back to the document
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Page", "Document", RuntimeId: [1]));

        Assert.Equal(offset, _controller.Cursor!.TextOffset);
        Assert.Same(node, _quickNav.CurrentNode);
    }

    [Fact]
    public void FocusOnThePageItself_FromAnEditField_ReturnsToBrowseMode()
    {
        LoadDocument();
        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Search", "Edit", RuntimeId: [5]));
        Assert.Equal(InteractionMode.Focus, _navigationManager.CurrentMode);
        var offset = _controller.Cursor!.TextOffset;

        _controller.HandleFocusChanged(new FocusChangedEvent(DateTimeOffset.UtcNow, "Page", "Document", RuntimeId: [1]));

        Assert.Equal(InteractionMode.Browse, _navigationManager.CurrentMode);
        Assert.Equal(offset, _controller.Cursor!.TextOffset);
    }
}
