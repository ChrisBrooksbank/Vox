using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Audio;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

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
    private bool _fullRecaptureRequested;

    // Reading position per document, so returning to a page resumes where the user was
    private const int RememberedDocuments = 8;
    private readonly LinkedList<(string Key, int Offset, int[]? NodeId)> _documentPositions = new();

    private const int UIA_NamePropertyId = 30005;
    private const int UIA_ExpandCollapseStatePropertyId = 30070;
    private const int UIA_ValueValuePropertyId = 30045;
    private const int UIA_ToggleToggleStatePropertyId = 30086;
    private const int UIA_SelectionItemIsSelectedPropertyId = 30079;

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
        ILogger<BrowseModeController> logger)
    {
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

        switch (command)
        {
            case NavigationCommand.StopSpeech:
                _speechQueue.CancelAll();
                return;

            case NavigationCommand.SayAll:
            case NavigationCommand.ElementsList:
            case NavigationCommand.ReadCurrentLine:
            case NavigationCommand.ReadCurrentWord:
            case NavigationCommand.ToggleMode:
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

            case NavigationCommand.ReadCurrentLine:
                Speak(LineText(_cursor!.ReadCurrentLine()));
                return;

            case NavigationCommand.ReadCurrentWord:
                Speak(LineText(_cursor!.ReadCurrentWord()));
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

        if (QuickNavHandler.IsQuickNavCommand(command))
        {
            var node = _quickNavHandler.Handle(command);
            if (node is not null)
            {
                _cursor?.MoveTo(node.TextRange.Start);
                Announce(node);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Keys and focus
    // -------------------------------------------------------------------------

    public void HandleRawKey(RawKeyEvent rawKey)
    {
        // Any key press stops Say All
        if (rawKey.Key.IsKeyDown && _sayAllController.IsReading)
            StopSayAll();

        // Typing echo only where keys actually type: not in browse mode over a document
        bool browsingDocument = _documentActive && _navigationManager.CurrentMode == InteractionMode.Browse;
        if (!browsingDocument)
            _typingEchoHandler.HandleKeyEvent(rawKey);
    }

    public void HandleFocusChanged(FocusChangedEvent focus)
    {
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

        _lastFocusedRuntimeId = runtimeId;

        // Modes only matter inside the active document: auto-switch and follow focus only there
        var node = _quickNavHandler.CurrentDocument?.FindByRuntimeId(runtimeId);
        if (node is null)
            return;

        // Tabbing or clicking into a text box, combo box or list box enters Focus mode, so typed
        // letters reach it instead of running quick-nav commands ("automatic focus mode")
        if (FormControls.NeedsFocusMode(node.ControlType, node.AriaRole)
            || FormControls.NeedsFocusMode(focus.ControlType, focus.AriaRole))
            _navigationManager.SwitchTo(InteractionMode.Focus, "focus moved to edit field");
        else
            _navigationManager.HandleFocusChanged(focus);

        MoveTo(node);
    }

    /// <summary>
    /// Announces state changes of the focused element: expanded/collapsed, and value or name
    /// changes of non-text controls (e.g. arrowing through a collapsed combo box).
    /// </summary>
    public void HandlePropertyChanged(PropertyChangedEvent evt)
    {
        var focus = _lastFocus;
        if (focus?.RuntimeId is null || !focus.RuntimeId.AsSpan().SequenceEqual(evt.RuntimeId))
            return;

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
                _focusedExpanded = state is 1 or 2;
                UpdateEscapeTarget();
                if (text is not null)
                    Speak(text);
                break;

            case UIA_ToggleToggleStatePropertyId:
                var toggleText = AnnouncementBuilder.ToggleStateText(ToInt(evt.NewValue));
                if (toggleText is not null)
                    Speak(toggleText);
                break;

            case UIA_SelectionItemIsSelectedPropertyId:
                if (evt.NewValue is bool selected)
                {
                    var selectedText = focus.ControlType == "RadioButton"
                        ? (selected ? "checked" : "not checked")
                        : (selected ? "selected" : null);
                    if (selectedText is not null)
                        Speak(selectedText);
                }
                break;

            case UIA_ValueValuePropertyId:
            case UIA_NamePropertyId:
                // Text controls change value on every keystroke; typing echo covers those
                if (focus.ControlType is "Edit" or "Document")
                    return;
                if (evt.NewValue is string value && !string.IsNullOrWhiteSpace(value))
                    Speak(value);
                break;
        }
    }

    /// <summary>
    /// Announces an item selected without a focus change (e.g. list selection following arrows).
    /// </summary>
    public void HandleElementSelected(ElementSelectedEvent evt)
    {
        // Selections that also moved focus were already announced by the focus event
        if (_lastFocus?.RuntimeId is { } focused && focused.AsSpan().SequenceEqual(evt.RuntimeId))
            return;
        if (!string.IsNullOrWhiteSpace(evt.Name))
            Speak(evt.Name);
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
        _fullRecaptureRequested = false;

        // A new (or no) document starts in Browse mode; nothing to announce. The focus a page sets
        // while loading (e.g. an autofocused search box) deliberately doesn't enter Focus mode.
        _navigationManager.ResetMode(InteractionMode.Browse);

        if (document is null)
        {
            _cursor = null;
        }
        else
        {
            _cursor = new VBufferCursor(document, _audioCuePlayer);
            var focused = evt.FocusedRuntimeId is { Length: > 0 } id ? document.FindByRuntimeId(id) : null;

            // Returning to a page with only the page itself focused: resume where the user was
            bool focusIsPage = focused is null || ReferenceEquals(focused, document.Root);
            if (!(focusIsPage && TryRestorePosition(document)) && focused is not null)
                MoveTo(focused);
        }

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
            _fullRecaptureRequested = false;

        // An ancestor's text depends on whether this subtree has text; refresh it too
        if (result.RecaptureRuntimeId is not null)
            _documentActions.RequestRecapture(result.RecaptureRuntimeId);

        // Keep the user's position: the same character of the same element if it still exists,
        // otherwise the same text, shifted by whatever was inserted or removed before it
        var oldNode = _quickNavHandler.CurrentNode;
        int oldOffset = _cursor.TextOffset;
        var node = oldNode is not null ? updated.FindByRuntimeId(oldNode.UIARuntimeId) : null;

        int newOffset;
        if (node is not null && oldNode is not null)
        {
            int within = Math.Max(0, oldOffset - oldNode.TextRange.Start);
            int newLength = node.TextRange.End - node.TextRange.Start;
            newOffset = node.TextRange.Start + (newLength > 0 ? Math.Min(within, newLength - 1) : within);
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
        else if (!_fullRecaptureRequested)
        {
            _fullRecaptureRequested = true;
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

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void UpdateDocumentActive()
    {
        bool active = _quickNavHandler.CurrentDocument is not null && !_modalOpen;
        if (active == _documentActive)
            return;

        _documentActive = active;
        DocumentActiveChanged?.Invoke(this, active);
    }

    private void UpdateEscapeTarget()
    {
        var focus = _lastFocus;
        var role = focus?.AriaRole?.Trim().ToLowerInvariant();
        bool popupOpen = focus is not null &&
            (((focus.ControlType == "ComboBox" || role == "combobox") && _focusedExpanded)
             || focus.ControlType is "Menu" or "MenuItem"
             || role is "menu" or "menuitem" or "menuitemcheckbox" or "menuitemradio");

        if (popupOpen == _escapeGoesToPage)
            return;
        _escapeGoesToPage = popupOpen;
        EscapeGoesToPageChanged?.Invoke(this, popupOpen);
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
        _documentPositions.AddFirst((key, _cursor.TextOffset, _quickNavHandler.CurrentNode?.UIARuntimeId));
        while (_documentPositions.Count > RememberedDocuments)
            _documentPositions.RemoveLast();
    }

    private bool TryRestorePosition(VBufferDocument document)
    {
        var key = DocumentKey(document);
        for (var entry = _documentPositions.First; entry is not null; entry = entry.Next)
        {
            if (entry.Value.Key != key)
                continue;

            _cursor!.MoveTo(entry.Value.Offset);
            _quickNavHandler.CurrentNode = entry.Value.NodeId is { } id
                ? document.FindByRuntimeId(id) ?? _cursor.CurrentNode
                : _cursor.CurrentNode;
            _sayAllCursor = null;
            return true;
        }
        return false;
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
        _ = ShowElementsListAsync(document);
    }

    private async Task ShowElementsListAsync(VBufferDocument document)
    {
        VBufferNode? selected = null;
        try
        {
            selected = await _elementsListPresenter.ShowAsync(document).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Elements List failed");
        }
        _pipeline.Post(new ElementsListClosedEvent(DateTimeOffset.UtcNow, selected));
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
        NavigationCommand.NextChar or NavigationCommand.PrevChar;

    private void MoveCaret(NavigationCommand command)
    {
        if (_cursor is null) return;

        string? text = command switch
        {
            NavigationCommand.NextLine => LineTextOrNull(_cursor.NextLine()),
            NavigationCommand.PrevLine => LineTextOrNull(_cursor.PrevLine()),
            NavigationCommand.NextWord => _cursor.NextWord(),
            NavigationCommand.PrevWord => _cursor.PrevWord(),
            NavigationCommand.NextChar => CharText(_cursor.NextChar()),
            NavigationCommand.PrevChar => CharText(_cursor.PrevChar()),
            _ => null,
        };

        // null means a boundary (the cursor already played the cue)
        if (text is null) return;

        _quickNavHandler.CurrentNode = _cursor.CurrentNode ?? _quickNavHandler.CurrentNode;

        // Entering a link, button or heading: say what it is, not just its text
        var role = RoleEnteredAtCursor();
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
            return profile.AnnounceControlType ? ControlTypeNames.ToSpoken(n.IsLink ? "Hyperlink" : n.ControlType) : null;
        }
        return null;
    }

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

    // User navigation always interrupts whatever is being spoken
    private void Speak(string text) =>
        _speechQueue.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
}
