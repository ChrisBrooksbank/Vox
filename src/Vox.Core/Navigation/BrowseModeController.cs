using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.Audio;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

using Vox.Core.Text;

namespace Vox.Core.Navigation;

/// <summary>
/// Routes navigation commands, key events and document events to the browse-mode components:
/// NavigationManager (mode), QuickNavHandler (element navigation), a shared VBufferCursor
/// (line/word/char navigation), SayAllController, the Elements List and element activation.
///
/// All Handle* methods are called on the EventPipeline thread, so the document, cursor and
/// QuickNavHandler state are only touched from that thread. Work that must happen elsewhere
/// (UIA activation, the WinForms dialog) reports back by posting pipeline events.
/// </summary>
public sealed class BrowseModeController
{
    private readonly SpeechQueue _speechQueue;
    private readonly IAudioCuePlayer _audioCuePlayer;
    private readonly NavigationManager _navigationManager;
    private readonly QuickNavHandler _quickNavHandler;
    private readonly SayAllController _sayAllController;
    private readonly AnnouncementBuilder _announcementBuilder;
    private readonly TypingEchoHandler _typingEchoHandler;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly IEventSink _pipeline;
    private readonly IBrowseDocumentActions _documentActions;
    private readonly IElementsListPresenter _elementsListPresenter;
    private readonly ILogger<BrowseModeController> _logger;
    private readonly IncrementalUpdater _incrementalUpdater = new();
    private readonly TableNavigator _tableNavigator;
    private readonly IFindPrompt? _findPrompt;
    private readonly FindInBuffer _find = new();
    private readonly BrowseSelection _selection = new();
    private readonly IClipboard? _clipboard;

    private VBufferCursor? _cursor;
    private VBufferCursor? _sayAllCursor;
    private bool _modalOpen;
    private bool _documentActive;
    private FocusChangedEvent? _lastFocus;
    private int[]? _lastFocusedRuntimeId;
    private int[]? _ignoreFocusReturnTo;
    private FocusChangedEvent? _unannouncedFocus;
    private bool _focusedExpanded;
    private bool _escapeGoesToPage;
    private long? _fullRecaptureRequestedAt;
    private int[]? _documentFocusedRuntimeId;
    // Focus is on an element the buffer doesn't contain (the address bar, another app, or a new
    // part of the page): browse keys are off until the tracker says focus is in the document
    private bool _focusOutsideDocument;

    // Reading position and mode per document, so returning to a page resumes where the user was
    private const int RememberedDocuments = 8;
    private readonly LinkedList<RememberedDocument> _documentPositions = new();

    private sealed record RememberedDocument(
        string Key,
        int Offset,
        int[]? NodeId,
        int OffsetInNode,
        InteractionMode Mode,
        int[]? FocusedRuntimeId);

    private readonly IFocusedTextReader? _focusedTextReader;
    private readonly RepeatPressCounter _readPresses = new();
    private readonly RepeatPressCounter _selectFromMarkPresses = new();
    private SpellMode _readSpell;

    private const int UIA_NamePropertyId = 30005;
    private const int UIA_ExpandCollapseStatePropertyId = 30070;
    private const int UIA_ValueValuePropertyId = 30045;
    private const int UIA_ToggleToggleStatePropertyId = 30086;
    private const int UIA_SelectionItemIsSelectedPropertyId = 30079;
    private const int UIA_RangeValueValuePropertyId = 30047;
    private const int UIA_IsEnabledPropertyId = 30010;

    // The last property change handled, to drop the copy a second subscription delivers
    private (int[] RuntimeId, int PropertyId, object? Value, long Tick)? _lastPropertyChange;
    private const int DuplicatePropertyChangeMs = 100;

    public BrowseModeController(
        SpeechQueue speechQueue,
        IAudioCuePlayer audioCuePlayer,
        NavigationManager navigationManager,
        QuickNavHandler quickNavHandler,
        SayAllController sayAllController,
        AnnouncementBuilder announcementBuilder,
        TypingEchoHandler typingEchoHandler,
        IOptionsMonitor<VoxSettings> settings,
        IEventSink pipeline,
        IBrowseDocumentActions documentActions,
        IElementsListPresenter elementsListPresenter,
        ILogger<BrowseModeController> logger,
        IFocusedTextReader? focusedTextReader = null,
        TableHeaderStore? tableHeaders = null,
        IFindPrompt? findPrompt = null,
        IClipboard? clipboard = null)
    {
        _clipboard = clipboard;
        _findPrompt = findPrompt;
        _focusedTextReader = focusedTextReader;
        _tableNavigator = new TableNavigator(tableHeaders);
        _speechQueue = speechQueue;
        _audioCuePlayer = audioCuePlayer;
        _navigationManager = navigationManager;
        _quickNavHandler = quickNavHandler;
        _sayAllController = sayAllController;
        _announcementBuilder = announcementBuilder;
        _typingEchoHandler = typingEchoHandler;
        _settings = settings;
        _pipeline = pipeline;
        _documentActions = documentActions;
        _elementsListPresenter = elementsListPresenter;
        _logger = logger;
    }

    /// <summary>
    /// Raised when browse-mode key handling should be enabled (a document has focus and no
    /// Vox dialog is open) or disabled.
    /// </summary>
    public event EventHandler<bool>? DocumentActiveChanged;

    /// <summary>True while a web document has focus and no Vox dialog is open.</summary>
    public bool IsDocumentActive => _documentActive;

    /// <summary>The virtual cursor for the active document, or null.</summary>
    public VBufferCursor? Cursor => _cursor;

    /// <summary>
    /// While browsing, the buffer for the review cursor, with its own copy of the browse cursor
    /// so it can be read on the UIA thread; null otherwise. Call on the pipeline thread.
    /// </summary>
    public ReviewTether? ReviewTether()
    {
        if (!_documentActive || _navigationManager.CurrentMode != InteractionMode.Browse || _cursor is null)
            return null;
        var copy = new VBufferCursor(_cursor.Document, _audioCuePlayer);
        ApplyCursorSettings(copy);
        copy.MoveTo(_cursor.TextOffset);
        return new ReviewTether(new BufferTextDocument(copy), _cursor.Document, _cursor.TextOffset);
    }

    /// <summary>
    /// How long after an unanswered full-document re-capture request another may be made
    /// (the capture can fail, and then no reply ever comes).
    /// </summary>
    public TimeSpan FullRecaptureRetryInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Raised when the user confirmed quitting Vox (the quit command pressed twice).</summary>
    public event EventHandler? QuitRequested;

    /// <summary>Raised when the user asked to run the first-run setup again.</summary>
    public event EventHandler? SetupRequested;

    private readonly Input.RepeatPressConfirmation _quitConfirmation = new(TimeSpan.FromSeconds(3));

    private string ModifierName =>
        _settings.CurrentValue.ModifierKey == ModifierKey.CapsLock ? "Caps Lock" : "Insert";

    /// <summary>
    /// Raised when Escape should go to the page (the focused control has an open popup such as an
    /// expanded combo box or a menu) rather than leave Focus mode.
    /// </summary>
    public event EventHandler<bool>? EscapeGoesToPageChanged;

    /// <summary>
    /// Whether the pipeline should speak <paramref name="focus"/>. Called after
    /// <see cref="HandleFocusChanged"/> has processed the same event: focus returning to the page
    /// after an Elements List jump is not announced, so it can't talk over the jump.
    /// </summary>
    public bool ShouldAnnounceFocus(FocusChangedEvent focus) => !ReferenceEquals(focus, _unannouncedFocus);

    // -------------------------------------------------------------------------
    // Commands
    // -------------------------------------------------------------------------

    public void HandleCommand(NavigationCommand command)
    {
        // Every command stops Say All (Insert+Down restarts it below)
        StopSayAll();

        // Anything but selecting, copying or reading ends the browse-mode selection
        if (!KeepsSelection(command))
            _selection.Clear();

        switch (command)
        {
            case NavigationCommand.StopSpeech:
                _speechQueue.CancelAll();
                return;

            case NavigationCommand.Quit:
                // Pressed twice within a few seconds: one accidental press must not close the
                // screen reader
                if (_quitConfirmation.Press())
                    QuitRequested?.Invoke(this, EventArgs.Empty);
                else
                    Speak($"Press {ModifierName} Q again to exit Vox");
                return;

            case NavigationCommand.RunSetup:
                SetupRequested?.Invoke(this, EventArgs.Empty);
                return;

            case NavigationCommand.ReadCurrentLine:
            case NavigationCommand.ReadCurrentWord:
            case NavigationCommand.ReadCurrentChar:
            case NavigationCommand.ReadSelection:
            case NavigationCommand.ReadFormatting:
                // Browse mode reads the buffer; anywhere else, the focused text control
                bool browsing = _documentActive && _navigationManager.CurrentMode == InteractionMode.Browse;
                // Pressed again quickly: spell, then spell phonetically
                _readSpell = Spelling.ForPress(_readPresses.Press((int)command), command switch
                {
                    NavigationCommand.ReadCurrentChar => TextUnit.Character,
                    NavigationCommand.ReadCurrentWord => TextUnit.Word,
                    _ => TextUnit.Line,
                });
                if (!browsing && _focusedTextReader is { HasFocusedText: true } reader)
                {
                    reader.Read(command switch
                    {
                        NavigationCommand.ReadCurrentLine => TextReadKind.Line,
                        NavigationCommand.ReadCurrentWord => TextReadKind.Word,
                        NavigationCommand.ReadCurrentChar => TextReadKind.Character,
                        NavigationCommand.ReadFormatting => TextReadKind.Formatting,
                        _ => TextReadKind.Selection,
                    }, _readSpell);
                    return;
                }
                if (_quickNavHandler.CurrentDocument is null || _cursor is null)
                {
                    Speak("No text");
                    return;
                }
                break;

            case NavigationCommand.SayAll:
                // In an edit control (outside browse mode), read it from the caret
                if (!(_documentActive && _navigationManager.CurrentMode == InteractionMode.Browse)
                    && _focusedTextReader is { HasFocusedText: true } sayAllReader)
                {
                    _sayAllController.Start(sayAllReader.CreateSayAllSource());
                    return;
                }
                if (_quickNavHandler.CurrentDocument is null || _cursor is null)
                {
                    Speak("Not in a document");
                    return;
                }
                break;

            case NavigationCommand.ElementsList:
            case NavigationCommand.ToggleMode:
            case NavigationCommand.Find:
            case NavigationCommand.FindNext:
            case NavigationCommand.FindPrevious:
                // These only apply to web documents; say so rather than silently eating the key
                if (_quickNavHandler.CurrentDocument is null || _cursor is null)
                {
                    Speak("Not in a document");
                    return;
                }
                break;
        }

        switch (command)
        {
            case NavigationCommand.SayAll:
                StartSayAll();
                return;

            case NavigationCommand.ElementsList:
                OpenElementsList();
                return;

            case NavigationCommand.Find:
                OpenFindPrompt();
                return;

            case NavigationCommand.FindNext:
            case NavigationCommand.FindPrevious:
                // Nothing searched for yet: ask what to find
                if (_find.Last is not { } last)
                    OpenFindPrompt();
                else
                    FindAndMove(last, forward: command == NavigationCommand.FindNext, fromCursor: false);
                return;

            case NavigationCommand.ReadCurrentLine:
                ApplyCursorSettings(_cursor!);
                if (_readSpell == SpellMode.None)
                    SpeakContent(LineText(_cursor!.ReadCurrentLine()));
                else
                    Speak(Spelling.Say(_cursor!.ReadCurrentLine(), TextUnit.Line, _readSpell));
                return;

            case NavigationCommand.ReadCurrentWord:
                if (_readSpell == SpellMode.None)
                    SpeakContent(LineText(_cursor!.ReadCurrentWord()));
                else
                    Speak(Spelling.Say(_cursor!.ReadCurrentWord(), TextUnit.Word, _readSpell));
                return;

            case NavigationCommand.ReadCurrentChar:
                if (_cursor!.CurrentChar == '\0')
                    Speak("blank");
                else
                    Speak(_readSpell == SpellMode.None ? CharText(_cursor.CurrentChar)!
                        : Spelling.Say(_cursor.CurrentChar.ToString(), TextUnit.Character, _readSpell));
                return;

            case NavigationCommand.ReadSelection:
                var selected = _selection.TextFor(_cursor!);
                if (selected.Length == 0)
                    Speak("No selection");
                else
                    Speak(_readSpell == SpellMode.None ? SelectionText(selected)
                        : Spelling.Say(selected, TextUnit.Line, _readSpell));
                return;

            case NavigationCommand.ReadFormatting:
                Speak("No formatting information"); // the buffer doesn't keep formatting
                return;
        }

        // Mode toggling, auto-switching and Focus-mode blocking. For activation, the element that
        // matters is the link/control around the cursor, not the text node it is on.
        var commandNode = command == NavigationCommand.ActivateElement
            ? ActivationTarget(_quickNavHandler.CurrentNode)
            : _quickNavHandler.CurrentNode;
        if (_navigationManager.HandleCommand(command, commandNode))
            return;

        if (_quickNavHandler.CurrentDocument is null)
            return;

        if (command == NavigationCommand.ActivateElement)
        {
            ActivateCurrentNode();
            return;
        }

        if (_navigationManager.CurrentMode != InteractionMode.Browse)
            return;

        if (IsCaretCommand(command))
        {
            MoveCaret(command);
            return;
        }

        if (BrowseSelection.IsSelectionCommand(command))
        {
            ExtendSelection(command);
            return;
        }

        if (command == NavigationCommand.CopySelection)
        {
            CopySelection();
            return;
        }

        if (command == NavigationCommand.MarkStart)
        {
            _selection.SetMark(_cursor!);
            Speak("Start marked");
            return;
        }

        if (command == NavigationCommand.SelectFromMark)
        {
            SelectFromMark();
            return;
        }

        if (TableNavigator.IsTableCommand(command))
        {
            MoveInTable(command);
            return;
        }

        if (QuickNavHandler.IsQuickNavCommand(command))
        {
            var node = _quickNavHandler.Handle(command);
            if (node is not null)
            {
                _cursor?.MoveTo(node.TextRange.Start);
                if (VBufferDocument.IsTable(node))
                    AnnounceTable(node);
                else if (QuickNavHandler.ElementKindName(command) is { } kind)
                    AnnounceElement(node, kind);
                else
                    Announce(node);
            }
            return;
        }

        if (QuickNavHandler.IsTextLineCommand(command))
        {
            if (_cursor is not null && _quickNavHandler.FindTextLine(command, _cursor.TextOffset) is { } start)
            {
                _cursor.MoveTo(start);
                _quickNavHandler.CurrentNode = _cursor.CurrentNode ?? _quickNavHandler.CurrentNode;
                SpeakContent(LineText(ParagraphAt(_cursor.Document.FlatText, start)));
            }
            return;
        }

        if ((command is NavigationCommand.EndOfContainer or NavigationCommand.StartOfContainer) && _cursor is not null)
            MoveToContainerEdge(end: command == NavigationCommand.EndOfContainer);
    }

    /// <summary>
    /// , moves past the end of the list, table, landmark, block quote or frame the cursor is in
    /// and reads on from there; Shift+, goes back to its start and says what it is.
    /// </summary>
    private void MoveToContainerEdge(bool end)
    {
        if (_quickNavHandler.FindContainerEdge(end, _cursor!.TextOffset) is not { } edge)
            return;
        var (container, offset) = edge;

        _cursor.MoveTo(offset);
        if (end)
        {
            _quickNavHandler.CurrentNode = _cursor.CurrentNode ?? _quickNavHandler.CurrentNode;
            SpeakContent(LineText(ParagraphAt(_cursor.Document.FlatText, offset)));
        }
        else if (VBufferDocument.IsTable(container))
            AnnounceTable(container);
        else if (container.IsLandmark)
            Announce(container);
        else
            AnnounceElement(container, PageElements.IsList(container) ? "list"
                : PageElements.IsBlockQuote(container) ? "block quote" : "frame");
    }

    /// <summary>
    /// Ctrl+Alt+arrows / Home / End: moves the cursor to the next cell of the table it is in and
    /// says the cell (with the changed row or column and headers); the boundary cue at the edge.
    /// Also reads the current row or column, and sets the header row or column.
    /// </summary>
    private void MoveInTable(NavigationCommand command)
    {
        var document = _quickNavHandler.CurrentDocument!;
        // The current element when it is in a cell (an empty cell's range sits at the next
        // cell's text); otherwise where the cursor is (after T, the current element is the table)
        var current = _quickNavHandler.CurrentNode;
        if (document.FindTableCell(current) is null)
            current = _cursor?.CurrentNode;

        var result = _tableNavigator.Handle(document, current, command);
        if (result.NotInTable)
        {
            Speak("Not in a table");
            return;
        }
        if (result.AtEdge)
        {
            _audioCuePlayer.Play("boundary");
            return;
        }

        if (result.Cell is { } cell)
            MoveTo(cell.Node);
        Speak(result.Text!);
    }

    /// <summary>
    /// Shift/Ctrl+Shift+arrows, Shift+Home/End, Ctrl+Shift+Home/End, Ctrl+A: moves the selection's
    /// active end and says "selected" or "unselected" with the text that changed.
    /// </summary>
    private void ExtendSelection(NavigationCommand command)
    {
        if (_cursor is null)
            return;
        ApplyCursorSettings(_cursor);
        if (_selection.Extend(_cursor, command) is not { } change)
        {
            _audioCuePlayer.Play("boundary");
            return;
        }
        _quickNavHandler.CurrentNode = _cursor.CurrentNode ?? _quickNavHandler.CurrentNode;
        // The cursor has moved on: Say All's last position no longer applies
        _sayAllCursor = null;

        if (command == NavigationCommand.SelectAll)
            Speak("selected all");
        else
            Speak($"{(change.Selected ? "selected" : "unselected")} {SelectionText(change.Text)}");
    }

    /// <summary>Ctrl+C: copies the selected text to the clipboard as plain text.</summary>
    private void CopySelection()
    {
        var text = _cursor is null ? string.Empty : _selection.TextFor(_cursor);
        if (text.Length == 0)
        {
            Speak("No selection");
            return;
        }
        if (_clipboard?.SetText(text.Replace("\n", Environment.NewLine)) != true)
        {
            _audioCuePlayer.Play("error");
            Speak("Could not copy");
            return;
        }
        Speak("Copied to clipboard");
    }

    /// <summary>
    /// Insert+F10: selects from the mark (Insert+F9) to the cursor and says it; pressed again
    /// quickly, copies it.
    /// </summary>
    private void SelectFromMark()
    {
        if (_cursor is null)
            return;
        bool again = _selectFromMarkPresses.Press(0) > 1;
        if (again && _selection.IsValidFor(_cursor))
        {
            CopySelection();
            return;
        }
        if (!_selection.SelectFromMark(_cursor))
        {
            Speak("No start marker set");
            return;
        }
        Speak($"selected {SelectionText(_selection.TextFor(_cursor))}");
    }

    // Longer than this, a selection is said by its length rather than read out
    private const int MaxSpokenSelection = 2000;

    /// <summary>How selected text is said: a character by name, blank text as "blank".</summary>
    private static string SelectionText(string text)
    {
        if (text.Length == 1)
            return CharText(text[0])!;
        if (string.IsNullOrWhiteSpace(text))
            return "blank";
        return text.Length > MaxSpokenSelection ? $"{text.Length} characters" : text;
    }

    private static bool KeepsSelection(NavigationCommand command) =>
        BrowseSelection.IsSelectionCommand(command) || command is
        NavigationCommand.CopySelection or NavigationCommand.ReadSelection or
        NavigationCommand.MarkStart or NavigationCommand.SelectFromMark or
        NavigationCommand.ReadCurrentLine or NavigationCommand.ReadCurrentWord or
        NavigationCommand.ReadCurrentChar or NavigationCommand.ReadFormatting or
        NavigationCommand.StopSpeech;

    /// <summary>The buffer line ('\n'-separated) starting at <paramref name="start"/>.</summary>
    private static string ParagraphAt(string text, int start)
    {
        int end = text.IndexOf('\n', start);
        return text.Substring(start, (end < 0 ? text.Length : end) - start);
    }

    // -------------------------------------------------------------------------
    // Keys and focus
    // -------------------------------------------------------------------------

    public void HandleRawKey(RawKeyEvent rawKey)
    {
        // Any key press stops Say All — except a bare modifier (Shift, Ctrl, Alt, the screen reader
        // key), which usually starts a command; the command itself decides
        if (rawKey.Key.IsKeyDown && _sayAllController.IsReading && !IsBareModifier(rawKey.Key.VkCode))
            StopSayAll();

        // Typing echo only where keys actually type: not in browse mode over a document
        bool browsingDocument = _documentActive && _navigationManager.CurrentMode == InteractionMode.Browse;
        if (!browsingDocument)
            _typingEchoHandler.HandleKeyEvent(rawKey);
    }

    /// <summary>
    /// Counts focus changes. Passed to the document tracker with each focus change and echoed
    /// back in <see cref="FocusInDocumentEvent"/>, so only the report for the latest focus counts.
    /// </summary>
    public long FocusSequence => _focusSequence;
    private long _focusSequence;

    public void HandleFocusChanged(FocusChangedEvent focus)
    {
        _focusSequence++;
        // Never echo what is typed into a password field; a new field starts a new word
        _typingEchoHandler.PasswordMode = focus.IsPassword;
        _typingEchoHandler.ResetWord();
        _lastFocus = focus;
        _focusedExpanded = focus.IsExpanded;
        UpdateEscapeTarget();

        // Focus moving around Vox's own Elements List must not move the cursor or change mode
        if (_modalOpen)
            return;

        var runtimeId = focus.RuntimeId;
        if (runtimeId is null || runtimeId.Length == 0)
            return;

        // Focus returning to where it was when the Elements List opened must not undo the jump,
        // nor be announced over it
        var ignore = _ignoreFocusReturnTo;
        _ignoreFocusReturnTo = null;
        if (ignore is not null && ignore.AsSpan().SequenceEqual(runtimeId))
        {
            _unannouncedFocus = focus;
            return;
        }

        // Chromium repeats focus events for an element that already has focus; only a real move
        // may switch modes (otherwise Escape to Browse mode would be undone by the next repeat)
        bool focusMoved = _lastFocusedRuntimeId is null || !_lastFocusedRuntimeId.AsSpan().SequenceEqual(runtimeId);
        _lastFocusedRuntimeId = runtimeId;

        // Modes only matter inside the active document: auto-switch and follow focus only there
        var node = _quickNavHandler.CurrentDocument?.FindByRuntimeId(runtimeId);
        if (node is null || ReferenceEquals(node, _quickNavHandler.CurrentDocument!.Root))
            _cursorFollowedFocusTo = null; // coming back from here is a real move again
        if (node is null)
        {
            // Probably left the page (Ctrl+L, F6, Alt+Tab). Stop treating keys as browse commands
            // at once, rather than after the tracker has walked the new focus's ancestors:
            // letters typed into the address bar must not be swallowed meanwhile
            if (_quickNavHandler.CurrentDocument is not null && !_focusOutsideDocument)
            {
                _focusOutsideDocument = true;
                UpdateDocumentActive();
            }
            return;
        }
        _documentFocusedRuntimeId = runtimeId;
        if (_focusOutsideDocument)
        {
            _focusOutsideDocument = false;
            UpdateDocumentActive();
        }

        if (focusMoved)
        {
            // Tabbing or clicking into a text box, combo box or list box enters Focus mode, so typed
            // letters reach it instead of running quick-nav commands ("automatic focus mode").
            // Cue only: speaking the mode would cut off the field's own announcement.
            if (FormControls.NeedsFocusMode(node.ControlType, node.AriaRole)
                || FormControls.NeedsFocusMode(focus.ControlType, focus.AriaRole))
                _navigationManager.SwitchTo(InteractionMode.Focus, "focus moved to edit field", announce: false);
            else
                _navigationManager.HandleFocusChanged(focus);
        }

        // Focus on the page itself (a dialog closed, the background clicked, a script's blur()):
        // the root's text starts at offset 0, so following it would send the user to the top
        if (ReferenceEquals(node, _quickNavHandler.CurrentDocument!.Root))
            return;

        // The cursor follows focus once per focused element: a repeated focus event for it
        // (Chromium sends them, e.g. when the window is re-activated) must not pull the cursor
        // back after the user has read on from it
        if (_cursorFollowedFocusTo is { } followed && followed.AsSpan().SequenceEqual(runtimeId))
            return;

        MoveTo(node);
        _cursorFollowedFocusTo = runtimeId;
    }

    // The focused element the cursor was last moved to (pipeline thread only)
    private int[]? _cursorFollowedFocusTo;

    /// <summary>
    /// Announces state changes of the focused element: expanded/collapsed, and value or name
    /// changes of non-text controls (e.g. arrowing through a collapsed combo box).
    /// </summary>
    public void HandlePropertyChanged(PropertyChangedEvent evt)
    {
        var focus = _lastFocus;
        if (focus?.RuntimeId is null || !focus.RuntimeId.AsSpan().SequenceEqual(evt.RuntimeId))
            return;

        // The focused element's own handler and the document's both report changes of a focused
        // page element: handle each change once
        var now = Environment.TickCount64;
        if (_lastPropertyChange is { } last && last.PropertyId == evt.PropertyId && Equals(last.Value, evt.NewValue)
            && now - last.Tick < DuplicatePropertyChangeMs && last.RuntimeId.AsSpan().SequenceEqual(evt.RuntimeId))
            return;
        _lastPropertyChange = (evt.RuntimeId, evt.PropertyId, evt.NewValue, now);

        // State changes (expanded, checked, selected) are queued after the focus announcement
        // rather than interrupting it, and skipped when the focus announcement already said it
        // (Chromium raises focus and then the new item's state change when arrowing a list)
        switch (evt.PropertyId)
        {
            case UIA_ExpandCollapseStatePropertyId:
                var state = ToInt(evt.NewValue);
                var text = state switch
                {
                    0 => "collapsed",
                    1 => "expanded",
                    2 => "partially expanded",
                    _ => null,
                };
                bool expanded = state is 1 or 2;
                bool alreadySaid = focus.IsExpandable && focus.IsExpanded == expanded;
                _focusedExpanded = expanded;
                _lastFocus = focus with { IsExpandable = state is 0 or 1 or 2, IsExpanded = expanded };
                UpdateEscapeTarget();
                if (text is not null && !alreadySaid)
                    SpeakQueued(text);
                break;

            case UIA_ToggleToggleStatePropertyId:
                var toggle = ToInt(evt.NewValue);
                var toggleText = AnnouncementBuilder.ToggleStateText(toggle);
                bool toggleSaid = focus.ToggleState == toggle;
                _lastFocus = focus with { ToggleState = toggle };
                if (toggleText is not null && !toggleSaid)
                    SpeakQueued(toggleText);
                break;

            case UIA_SelectionItemIsSelectedPropertyId:
                if (evt.NewValue is bool selected)
                {
                    bool selectionSaid = focus.IsSelected == selected;
                    _lastFocus = focus with { IsSelected = selected };
                    var selectedText = focus.ControlType == "RadioButton"
                        ? (selected ? "checked" : "not checked")
                        : (selected ? "selected" : null);
                    if (selectedText is not null && !selectionSaid)
                        SpeakQueued(selectedText);
                }
                break;

            case UIA_IsEnabledPropertyId:
                if (evt.NewValue is bool enabled)
                    SpeakQueued(enabled ? "available" : "unavailable");
                break;

            case UIA_RangeValueValuePropertyId:
                // Sliders and spin boxes; progress bars are reported by ProgressReporter
                if (focus.ControlType == "ProgressBar")
                    return;
                if (evt.NewValue is double range)
                    SpeakSelectionText(range.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture));
                break;

            case UIA_ValueValuePropertyId:
            case UIA_NamePropertyId:
                // Text controls change value on every keystroke; typing echo covers those. That
                // includes editable combo boxes (autocomplete and search boxes, role=combobox)
                if (focus.ControlType is "Edit" or "Document" || focus.IsValueReadOnly == false)
                    return;
                if (evt.PropertyId == UIA_ValueValuePropertyId && evt.NewValue is string newValue)
                    _lastFocus = focus with { Value = newValue };
                // Values interrupt, so rapid arrowing through a combo box speaks only the latest
                if (evt.NewValue is string value && !string.IsNullOrWhiteSpace(value))
                    SpeakSelectionText(value);
                break;
        }
    }

    // What the last selection or value change of the focused control said, and when: a collapsed
    // combo box reports each arrow press both as an option selected and as its value changing
    private string? _lastSelectionText;
    private long _lastSelectionTick;
    private const int DuplicateSelectionMs = 300;

    private void SpeakSelectionText(string text)
    {
        var now = Environment.TickCount64;
        bool duplicate = string.Equals(text.Trim(), _lastSelectionText, StringComparison.Ordinal)
            && now - _lastSelectionTick < DuplicateSelectionMs;
        _lastSelectionText = text.Trim();
        _lastSelectionTick = now;
        if (!duplicate)
            Speak(text);
    }

    /// <summary>
    /// True for property changes that alter what the buffer holds for the element (expanded,
    /// checked, selected, name, value), so the element must be re-captured.
    /// </summary>
    public static bool ChangesBufferText(int propertyId) => propertyId is
        UIA_ExpandCollapseStatePropertyId or UIA_ToggleToggleStatePropertyId or
        UIA_SelectionItemIsSelectedPropertyId or UIA_NamePropertyId or UIA_ValueValuePropertyId;

    /// <summary>
    /// Announces an item selected without a focus change (e.g. list selection following arrows).
    /// </summary>
    public void HandleElementSelected(ElementSelectedEvent evt)
    {
        // Selections that also moved focus were already announced by the focus event
        if (_lastFocus?.RuntimeId is { } focused && focused.AsSpan().SequenceEqual(evt.RuntimeId))
            return;
        if (!string.IsNullOrWhiteSpace(evt.Name))
            SpeakSelectionText(evt.Name);
    }

    // -------------------------------------------------------------------------
    // Document lifecycle
    // -------------------------------------------------------------------------

    public void HandleDocumentChanged(DocumentChangedEvent evt)
    {
        StopSayAll();
        RememberPosition();

        var document = evt.Document;
        _quickNavHandler.SetDocument(document);
        _fullRecaptureRequestedAt = null;
        _tableNavigator.Reset();

        // A new (or no) document starts in Browse mode; nothing to announce. The focus a page sets
        // while loading (e.g. an autofocused search box) deliberately doesn't enter Focus mode.
        _navigationManager.ResetMode(InteractionMode.Browse);

        _documentFocusedRuntimeId = null;
        _focusOutsideDocument = false;
        _cursorFollowedFocusTo = null;

        if (document is null)
        {
            _cursor = null;
        }
        else
        {
            _cursor = new VBufferCursor(document, _audioCuePlayer);
            var focused = evt.FocusedRuntimeId is { Length: > 0 } id ? document.FindByRuntimeId(id) : null;
            if (focused is not null)
                _documentFocusedRuntimeId = focused.UIARuntimeId;

            // A remembered document means the user is coming back to it, not loading it
            var remembered = FindRemembered(document);

            // Returning to a page with only the page itself focused: resume where the user was
            bool focusIsPage = focused is null || ReferenceEquals(focused, document.Root);
            if (focusIsPage && remembered is not null)
                RestorePosition(document, remembered);
            else if (focused is not null)
            {
                MoveTo(focused);
                _cursorFollowedFocusTo = focused.UIARuntimeId;
            }

            // Coming back to a page with focus in an edit field (Alt+Tab, tab switch): resume
            // typing in Focus mode. Only a fresh load's autofocus stays in Browse mode.
            if (remembered is not null && focused is not null && !focusIsPage)
            {
                bool wasFocusedThere = remembered.Mode == InteractionMode.Focus
                    && remembered.FocusedRuntimeId is { } previous
                    && previous.AsSpan().SequenceEqual(focused.UIARuntimeId);
                if (wasFocusedThere || FormControls.NeedsFocusMode(focused.ControlType, focused.AriaRole))
                    _navigationManager.SwitchTo(InteractionMode.Focus, "returned to edit field", announce: false);
            }
        }

        UpdateDocumentActive();
    }

    /// <summary>
    /// The tracker found the focused element inside the current document (e.g. a dialog added
    /// since the last capture): browse keys apply again.
    /// </summary>
    public void HandleFocusInDocument(FocusInDocumentEvent evt)
    {
        var document = _quickNavHandler.CurrentDocument;
        if (!_focusOutsideDocument || document is null)
            return;
        if (!evt.DocumentRuntimeId.AsSpan().SequenceEqual(document.Root.UIARuntimeId))
            return;
        // A report about an earlier focus must not re-enable keys after focus has moved on. The
        // tracker reads focus again itself, so its element may differ from the event's: go by the
        // focus change it was asked about when it says (sequence), else by element
        bool current = evt.FocusSequence > 0
            ? evt.FocusSequence == _focusSequence
            : _lastFocusedRuntimeId is not null && _lastFocusedRuntimeId.AsSpan().SequenceEqual(evt.FocusedRuntimeId);
        if (!current)
            return;

        _focusOutsideDocument = false;
        UpdateDocumentActive();
    }

    public void HandleSubtreeChanged(SubtreeChangedEvent evt)
    {
        var document = _quickNavHandler.CurrentDocument;
        if (document is null || _cursor is null)
            return;

        // An update captured for a different document (e.g. one that replaced it since) is stale
        if (evt.DocumentRuntimeId is { } documentId && !documentId.AsSpan().SequenceEqual(document.Root.UIARuntimeId))
            return;

        var result = _incrementalUpdater.ApplyUpdateDetailed(document, evt.RuntimeId, evt.NewSubtree);
        var updated = result.Document;
        if (ReferenceEquals(updated, document))
        {
            if (evt.NewSubtree is not null && document.FindByRuntimeId(evt.RuntimeId) is null)
                RecaptureUnknownElement(document, evt);
            return;
        }

        if (evt.RuntimeId.AsSpan().SequenceEqual(document.Root.UIARuntimeId))
            _fullRecaptureRequestedAt = null;

        // An ancestor's text depends on whether this subtree has text; refresh it too
        if (result.RecaptureRuntimeId is not null)
            _documentActions.RequestRecapture(result.RecaptureRuntimeId);

        // Keep the user's position: the same character of the same element if it still exists,
        // otherwise the same text, shifted by whatever was inserted or removed before it
        var oldNode = _quickNavHandler.CurrentNode;
        int oldOffset = _cursor.TextOffset;
        var node = oldNode is not null ? updated.FindByRuntimeId(oldNode.UIARuntimeId) : null;
        bool selecting = _selection.IsValidFor(_cursor);
        _selection.RebaseMark(document, updated, result.OldTextStart, result.OldTextEnd, result.TextDelta);

        int newOffset;
        if (node is not null && oldNode is not null)
        {
            int within = Math.Max(0, oldOffset - oldNode.TextRange.Start);
            int newLength = node.TextRange.End - node.TextRange.Start;
            newOffset = node.TextRange.Start + (newLength > 0 ? Math.Min(within, newLength - 1) : 0);
        }
        else if (oldOffset >= result.OldTextEnd)
        {
            newOffset = oldOffset + result.TextDelta;
        }
        else if (oldOffset >= result.OldTextStart)
        {
            newOffset = result.OldTextStart; // the text under the cursor was replaced
        }
        else
        {
            newOffset = oldOffset;
        }

        _quickNavHandler.SetDocument(updated);
        _cursor.SetDocument(updated, newOffset);
        if (selecting)
            _selection.Rebase(updated, result.OldTextStart, result.OldTextEnd, result.TextDelta, _cursor.TextOffset);
        else
            _selection.Clear();
        if (oldNode is not null)
            _quickNavHandler.CurrentNode = node ?? _cursor.CurrentNode;
    }

    /// <summary>
    /// A changed element the buffer doesn't contain (e.g. added since the last capture): re-capture
    /// its nearest ancestor the buffer knows, or — once, until it arrives — the whole document.
    /// </summary>
    private void RecaptureUnknownElement(VBufferDocument document, SubtreeChangedEvent evt)
    {
        var knownAncestor = evt.AncestorRuntimeIds?.FirstOrDefault(id => document.FindByRuntimeId(id) is not null);
        if (knownAncestor is not null)
        {
            _documentActions.RequestRecapture(knownAncestor);
        }
        else if (_fullRecaptureRequestedAt is not { } requestedAt
                 || Environment.TickCount64 - requestedAt >= (long)FullRecaptureRetryInterval.TotalMilliseconds)
        {
            // Once per interval: a re-capture that failed never replies, so don't wait forever
            _fullRecaptureRequestedAt = Environment.TickCount64;
            _documentActions.RequestRecapture(null);
        }
    }

    public void HandleElementsListClosed(ElementsListClosedEvent evt)
    {
        _modalOpen = false;
        UpdateDocumentActive();

        var selected = evt.SelectedNode;
        var document = _quickNavHandler.CurrentDocument;
        if (selected is null || document is null)
            return;

        // The document may have been updated or replaced while the dialog was open
        var node = document.FindByRuntimeId(selected.UIARuntimeId);
        if (node is null)
        {
            _audioCuePlayer.Play("error");
            Speak("Element no longer on page");
            return;
        }
        MoveTo(node);
        Announce(node);
    }

    public void HandleFindPromptClosed(FindPromptClosedEvent evt)
    {
        _modalOpen = false;
        UpdateDocumentActive();

        if (evt.Request is not { Text.Length: > 0 } request)
            return;
        _find.Remember(request);
        if (_quickNavHandler.CurrentDocument is null || _cursor is null)
            return;
        FindAndMove(request, forward: true, fromCursor: true);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void UpdateDocumentActive()
    {
        bool active = _quickNavHandler.CurrentDocument is not null && !_modalOpen && !_focusOutsideDocument;
        if (active == _documentActive)
            return;

        _documentActive = active;
        DocumentActiveChanged?.Invoke(this, active);
    }

    private void UpdateEscapeTarget()
    {
        var focus = _lastFocus;
        var role = focus?.AriaRole?.Trim().ToLowerInvariant();
        bool isMenu = focus is not null && (focus.ControlType == "Menu" || role == "menu");
        bool isMenuItem = focus is not null
            && (focus.ControlType == "MenuItem" || role is "menuitem" or "menuitemcheckbox" or "menuitemradio");

        // Escape belongs to the page only while a popup is open: an expanded combo box or menu
        // item, a popup menu itself, or an item inside one. A menu bar item (many sites use
        // role=menubar for plain navigation) or a closed menu's button must let Escape leave
        // Focus mode, or the user is stuck in it
        bool popupOpen = focus is not null &&
            (((focus.ControlType == "ComboBox" || role == "combobox" || isMenuItem) && _focusedExpanded)
             || isMenu
             || (isMenuItem && IsInPopupMenu(focus)));

        if (popupOpen == _escapeGoesToPage)
            return;
        _escapeGoesToPage = popupOpen;
        EscapeGoesToPageChanged?.Invoke(this, popupOpen);
    }

    /// <summary>
    /// Whether a focused menu item is inside a popup menu (its nearest menu-like ancestor is a
    /// menu, not a menu bar). An item the buffer doesn't contain counts as in a popup: popups
    /// are usually added to the page after it was captured.
    /// </summary>
    private bool IsInPopupMenu(FocusChangedEvent focus)
    {
        if (focus.RuntimeId is not { Length: > 0 } id)
            return true;
        var node = _quickNavHandler.CurrentDocument?.FindByRuntimeId(id);
        if (node is null)
            return true;

        for (var n = node.Parent; n is not null; n = n.Parent)
        {
            var role = n.AriaRole?.Trim().ToLowerInvariant();
            if (n.ControlType == "MenuBar" || role == "menubar")
                return false;
            if (n.ControlType == "Menu" || role == "menu")
                return true;
        }
        return false;
    }

    private static int ToInt(object? value) => value is IConvertible c ? c.ToInt32(null) : -1;

    private static string DocumentKey(VBufferDocument document) => string.Join(",", document.Root.UIARuntimeId);

    private void RememberPosition()
    {
        var document = _quickNavHandler.CurrentDocument;
        if (document is null || _cursor is null)
            return;

        var key = DocumentKey(document);
        RemovePosition(key);
        var node = _quickNavHandler.CurrentNode;
        _documentPositions.AddFirst(new RememberedDocument(
            key,
            _cursor.TextOffset,
            node?.UIARuntimeId,
            node is null ? 0 : Math.Max(0, _cursor.TextOffset - node.TextRange.Start),
            _navigationManager.CurrentMode,
            _documentFocusedRuntimeId));
        while (_documentPositions.Count > RememberedDocuments)
            _documentPositions.RemoveLast();
    }

    private RememberedDocument? FindRemembered(VBufferDocument document)
    {
        var key = DocumentKey(document);
        for (var entry = _documentPositions.First; entry is not null; entry = entry.Next)
        {
            if (entry.Value.Key == key)
                return entry.Value;
        }
        return null;
    }

    /// <summary>
    /// Puts the cursor back where it was. The page may have changed while the user was away, so
    /// the position is kept relative to the element that was current; the raw offset is only a
    /// fallback when that element is gone.
    /// </summary>
    private void RestorePosition(VBufferDocument document, RememberedDocument remembered)
    {
        var node = remembered.NodeId is { } id ? document.FindByRuntimeId(id) : null;
        if (node is not null)
        {
            int length = node.TextRange.End - node.TextRange.Start;
            int within = length > 0 ? Math.Min(remembered.OffsetInNode, length - 1) : 0;
            _cursor!.MoveTo(node.TextRange.Start + within);
            _quickNavHandler.CurrentNode = node;
        }
        else
        {
            _cursor!.MoveTo(remembered.Offset);
            _quickNavHandler.CurrentNode = _cursor.CurrentNode;
        }
        _sayAllCursor = null;
    }

    private void RemovePosition(string key)
    {
        for (var entry = _documentPositions.First; entry is not null; entry = entry.Next)
        {
            if (entry.Value.Key == key)
            {
                _documentPositions.Remove(entry);
                return;
            }
        }
    }

    private static bool IsBareModifier(int vk) =>
        KeyStateTracker.IsModifierKey(vk) || vk is KeyStateTracker.VK_INSERT or KeyStateTracker.VK_CAPITAL;

    /// <summary>
    /// The element Enter acts on: the nearest link, form control or focusable element at or above
    /// <paramref name="node"/> (after arrowing, the cursor is on a link's or button's text child).
    /// The document root itself is never the target.
    /// </summary>
    private static VBufferNode? ActivationTarget(VBufferNode? node)
    {
        for (var n = node; n?.Parent is not null; n = n.Parent)
        {
            if (n.IsLink || n.IsFocusable || FormControls.IsFormField(n.ControlType, n.AriaRole))
                return n;
        }
        return node;
    }

    private void MoveTo(VBufferNode node)
    {
        _quickNavHandler.CurrentNode = node;
        _cursor?.MoveTo(node.TextRange.Start);

        // The cursor has moved on: Say All's last position no longer applies
        _sayAllCursor = null;
    }

    private void StartSayAll()
    {
        var document = _quickNavHandler.CurrentDocument;
        if (document is null || _cursor is null)
        {
            _logger.LogDebug("SayAll requested but no document is loaded");
            return;
        }

        // Say All reads with its own cursor so document updates on this thread never race it;
        // the main cursor catches up when reading stops.
        _sayAllCursor = new VBufferCursor(document, _audioCuePlayer) { PlayCues = false };
        ApplyCursorSettings(_sayAllCursor);
        _sayAllCursor.MoveTo(_cursor.TextOffset);
        _sayAllController.Start(_sayAllCursor);
    }

    private void StopSayAll()
    {
        var sayAllCursor = _sayAllCursor;
        _sayAllCursor = null;
        _sayAllController.Cancel();

        if (sayAllCursor is null)
            return;

        // Leave the virtual cursor where reading stopped
        var readNode = sayAllCursor.CurrentNode;
        var document = _quickNavHandler.CurrentDocument;
        if (readNode is null || document is null || _cursor is null)
            return;

        var node = document.FindByRuntimeId(readNode.UIARuntimeId);
        if (node is null)
            return;

        _quickNavHandler.CurrentNode = node;
        _cursor.MoveTo(node.TextRange.Start + (sayAllCursor.TextOffset - readNode.TextRange.Start));
    }

    private void OpenElementsList()
    {
        var document = _quickNavHandler.CurrentDocument;
        if (document is null || _modalOpen)
            return;

        _modalOpen = true;
        _ignoreFocusReturnTo = _lastFocusedRuntimeId;
        UpdateDocumentActive();
        _ = ShowElementsListAsync(document, _quickNavHandler.CurrentNode ?? _cursor?.CurrentNode);
    }

    private async Task ShowElementsListAsync(VBufferDocument document, VBufferNode? currentNode)
    {
        VBufferNode? selected = null;
        try
        {
            selected = await _elementsListPresenter.ShowAsync(document, currentNode).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Elements List failed");
        }
        _pipeline.Post(new ElementsListClosedEvent(DateTimeOffset.UtcNow, selected));
    }

    private void OpenFindPrompt()
    {
        if (_findPrompt is null || _modalOpen)
            return;

        _modalOpen = true;
        _ignoreFocusReturnTo = _lastFocusedRuntimeId;
        UpdateDocumentActive();
        _ = ShowFindPromptAsync(_findPrompt, _find.History.ToList(), _find.Last?.MatchCase ?? false);
    }

    private async Task ShowFindPromptAsync(IFindPrompt prompt, IReadOnlyList<string> history, bool matchCase)
    {
        FindRequest? request = null;
        try
        {
            request = await prompt.ShowAsync(history, matchCase).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Find prompt failed");
        }
        _pipeline.Post(new FindPromptClosedEvent(DateTimeOffset.UtcNow, request));
    }

    /// <summary>
    /// Moves the cursor to the next (or previous) match and reads its line; the wrap cue when the
    /// search went past the end of the page. A new search starts at the cursor; next and previous
    /// start just past it, so they don't find the match the cursor is on.
    /// </summary>
    private void FindAndMove(FindRequest request, bool forward, bool fromCursor)
    {
        var cursor = _cursor!;
        int start = forward && !fromCursor ? cursor.TextOffset + 1 : cursor.TextOffset;
        if (FindInBuffer.Find(cursor.Document.FlatText, request.Text, start, forward, request.MatchCase) is not { } match)
        {
            _audioCuePlayer.Play("error");
            Speak($"{request.Text} not found");
            return;
        }

        if (match.Wrapped)
            _audioCuePlayer.Play("wrap");
        cursor.MoveTo(match.Offset);
        _quickNavHandler.CurrentNode = cursor.CurrentNode ?? _quickNavHandler.CurrentNode;
        // The cursor has moved on: Say All's last position no longer applies
        _sayAllCursor = null;
        ApplyCursorSettings(cursor);
        SpeakContent(LineText(cursor.ReadCurrentLine()));
    }

    private void ActivateCurrentNode()
    {
        var node = ActivationTarget(_quickNavHandler.CurrentNode);
        if (node is null)
        {
            _audioCuePlayer.Play("error");
            return;
        }
        _ = ActivateAsync(node);
    }

    private async Task ActivateAsync(VBufferNode node)
    {
        try
        {
            if (!await _documentActions.ActivateAsync(node).ConfigureAwait(false))
                _audioCuePlayer.Play("error");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to activate {Node}", node);
            _audioCuePlayer.Play("error");
        }
    }

    private static bool IsCaretCommand(NavigationCommand command) => command is
        NavigationCommand.NextLine or NavigationCommand.PrevLine or
        NavigationCommand.NextWord or NavigationCommand.PrevWord or
        NavigationCommand.NextChar or NavigationCommand.PrevChar or
        NavigationCommand.StartOfLine or NavigationCommand.EndOfLine or
        NavigationCommand.TopOfDocument or NavigationCommand.BottomOfDocument or
        NavigationCommand.NextParagraph or NavigationCommand.PrevParagraph;

    private void MoveCaret(NavigationCommand command)
    {
        if (_cursor is null) return;
        ApplyCursorSettings(_cursor);
        var from = _cursor.CurrentNode;

        string? text = command switch
        {
            NavigationCommand.NextLine => LineTextOrNull(_cursor.NextLine()),
            NavigationCommand.PrevLine => LineTextOrNull(_cursor.PrevLine()),
            NavigationCommand.NextWord => _cursor.NextWord(),
            NavigationCommand.PrevWord => _cursor.PrevWord(),
            NavigationCommand.NextChar => CharText(_cursor.NextChar()),
            NavigationCommand.PrevChar => CharText(_cursor.PrevChar()),
            NavigationCommand.StartOfLine => _cursor.StartOfLine() is { } first ? CharText(first) : "blank",
            NavigationCommand.EndOfLine => _cursor.EndOfLine() is { } last ? CharText(last) : "blank",
            NavigationCommand.TopOfDocument => LineText(_cursor.TopOfDocument()),
            NavigationCommand.BottomOfDocument => LineText(_cursor.BottomOfDocument()),
            NavigationCommand.NextParagraph => LineTextOrNull(_cursor.NextParagraph()),
            NavigationCommand.PrevParagraph => LineTextOrNull(_cursor.PrevParagraph()),
            _ => null,
        };

        // null means a boundary (the cursor already played the cue)
        if (text is null) return;

        _quickNavHandler.CurrentNode = _cursor.CurrentNode ?? _quickNavHandler.CurrentNode;

        // Entering a link, button or heading: say what it is, not just its text
        var role = RoleEnteredAtCursor();
        // Entering or leaving a list: "out of list", "list with 3 items" before the text
        var list = IsCharacterCommand(command) || !VerbosityProfile.For(_settings.CurrentValue.VerbosityLevel).AnnounceControlType
            ? null
            : ListAnnouncer.Transition(from, _cursor.CurrentNode);
        if (list is not null)
            text = $"{list}, {text}";
        // Page text alone is said in its language; with a role or list (said in Vox's) it isn't
        if (role is null && list is null && !IsCharacterCommand(command))
            SpeakContent(text);
        else
            Speak(role is null ? text : $"{text}, {role}");
    }

    /// <summary>
    /// The role of the link, button or heading whose text starts exactly at the cursor, if any
    /// (so it is spoken when the cursor enters it, not on every move inside it).
    /// </summary>
    private string? RoleEnteredAtCursor()
    {
        var profile = VerbosityProfile.For(_settings.CurrentValue.VerbosityLevel);
        int depth = 0;
        for (var n = _cursor?.CurrentNode; n?.Parent is not null && depth < 4; n = n.Parent, depth++)
        {
            bool isRole = n.IsLink || n.IsHeading || n.ControlType == "Button";
            if (!isRole)
                continue;
            if (_cursor!.TextOffset != n.TextRange.Start)
                return null;

            if (n.IsHeading)
                return profile.AnnounceHeadingLevel ? $"heading level {n.HeadingLevel}" : null;
            return profile.SpeaksRoleOf(n.ControlType, n.AriaRole, n.IsLink)
                ? ControlTypeNames.ToSpoken(n.IsLink ? "Hyperlink" : n.ControlType)
                : null;
        }
        return null;
    }

    // Settings are read at use time, so a changed line length applies at once
    private void ApplyCursorSettings(VBufferCursor cursor) =>
        cursor.MaxLineLength = Math.Max(0, _settings.CurrentValue.MaxLineLength);

    private static string? LineTextOrNull(string? line) => line is null ? null : LineText(line);

    private static string LineText(string line) => string.IsNullOrWhiteSpace(line) ? "blank" : line;

    private static string? CharText(char? ch) => ch is { } c ? TypingEchoHandler.GetCharacterName(c) : null;

    private void Announce(VBufferNode node)
    {
        var settings = _settings.CurrentValue;
        var text = _announcementBuilder.Build(
            node, VerbosityProfile.For(settings.VerbosityLevel), settings.AnnounceVisitedLinks);
        if (!string.IsNullOrWhiteSpace(text))
            Speak(text);
    }

    /// <summary>"Prices, table" followed by the table's first line (its first cell or caption).</summary>
    private void AnnounceTable(VBufferNode table)
    {
        var settings = _settings.CurrentValue;
        var text = _announcementBuilder.Build(
            table, VerbosityProfile.For(settings.VerbosityLevel), settings.AnnounceVisitedLinks);
        if (string.IsNullOrWhiteSpace(text))
            text = "table";
        // Only a table with text of its own: an empty one's range sits at the following content
        if (_cursor is not null && table.TextRange.Start < _cursor.Document.FlatText.Length && SubtreeHasText(table))
        {
            ApplyCursorSettings(_cursor);
            var firstLine = _cursor.ReadCurrentLine();
            if (!string.IsNullOrWhiteSpace(firstLine))
                text = $"{text}, {firstLine}";
        }
        Speak(text);
    }

    /// <summary>
    /// "Fruit, list" followed by the element's first line ("list item, Apples"), unless that line
    /// is only its name (an image's alt text).
    /// </summary>
    private void AnnounceElement(VBufferNode node, string kind)
    {
        var name = node.Name.Trim();
        var text = string.IsNullOrEmpty(name) ? kind : $"{name}, {kind}";
        // Only an element with text of its own: an empty one's range sits at the following content
        if (_cursor is not null && node.TextRange.Start < _cursor.Document.FlatText.Length && SubtreeHasText(node))
        {
            ApplyCursorSettings(_cursor);
            var firstLine = _cursor.ReadCurrentLine().Trim();
            if (!string.IsNullOrWhiteSpace(firstLine) && firstLine != name)
                text = $"{text}, {firstLine}";
        }
        Speak(text);
    }

    private static bool SubtreeHasText(VBufferNode node)
    {
        if (node.HasText)
            return true;
        foreach (var child in node.Children)
        {
            if (SubtreeHasText(child))
                return true;
        }
        return false;
    }

    // User navigation always interrupts whatever is being spoken
    private void Speak(string text) =>
        _speechQueue.Enqueue(new Utterance(text, SpeechPriority.Interrupt));

    /// <summary>Speaks page text, tagged with the language at the cursor.</summary>
    private void SpeakContent(string text) =>
        _speechQueue.Enqueue(new Utterance(text, SpeechPriority.Interrupt)
        {
            Language = _cursor?.CurrentNode?.Language is { Length: > 0 } language ? language : null,
        });

    // Characters are said by name ("space", "dot"), in Vox's language
    private static bool IsCharacterCommand(NavigationCommand command) => command is
        NavigationCommand.NextChar or NavigationCommand.PrevChar or
        NavigationCommand.StartOfLine or NavigationCommand.EndOfLine;

    // Follow-up information (state changes) waits for what is being said
    private void SpeakQueued(string text) =>
        _speechQueue.Enqueue(new Utterance(text, SpeechPriority.High));
}
