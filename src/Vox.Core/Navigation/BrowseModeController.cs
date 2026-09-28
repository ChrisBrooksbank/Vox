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
    private int[]? _lastFocusedRuntimeId;
    private int[]? _ignoreFocusReturnTo;

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
                StartSayAll();
                return;

            case NavigationCommand.ElementsList:
                OpenElementsList();
                return;

            case NavigationCommand.ReadCurrentLine:
                if (_cursor is not null)
                    Speak(LineText(_cursor.ReadCurrentLine()));
                return;

            case NavigationCommand.ReadCurrentWord:
                if (_cursor is not null)
                    Speak(LineText(_cursor.ReadCurrentWord()));
                return;
        }

        // Mode toggling, auto-switching and Focus-mode blocking
        if (_navigationManager.HandleCommand(command, _quickNavHandler.CurrentNode))
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
        _navigationManager.HandleFocusChanged(focus);

        var runtimeId = focus.RuntimeId;
        if (runtimeId is null || runtimeId.Length == 0)
            return;

        // Focus returning to where it was when the Elements List opened must not undo the jump
        var ignore = _ignoreFocusReturnTo;
        _ignoreFocusReturnTo = null;
        if (ignore is not null && ignore.AsSpan().SequenceEqual(runtimeId))
            return;

        _lastFocusedRuntimeId = runtimeId;

        // The virtual cursor follows system focus within the document
        var node = _quickNavHandler.CurrentDocument?.FindByRuntimeId(runtimeId);
        if (node is not null)
            MoveTo(node);
    }

    // -------------------------------------------------------------------------
    // Document lifecycle
    // -------------------------------------------------------------------------

    public void HandleDocumentChanged(DocumentChangedEvent evt)
    {
        StopSayAll();

        var document = evt.Document;
        _quickNavHandler.SetDocument(document);

        if (document is null)
        {
            _cursor = null;
        }
        else
        {
            _cursor = new VBufferCursor(document, _audioCuePlayer);
            var focused = evt.FocusedRuntimeId is { Length: > 0 } id ? document.FindByRuntimeId(id) : null;
            if (focused is not null)
                MoveTo(focused);
        }

        UpdateDocumentActive();
    }

    public void HandleSubtreeChanged(SubtreeChangedEvent evt)
    {
        var document = _quickNavHandler.CurrentDocument;
        if (document is null || _cursor is null)
            return;

        var updated = _incrementalUpdater.ApplyUpdate(document, evt.RuntimeId, evt.NewSubtree);
        if (ReferenceEquals(updated, document))
            return;

        // Keep the user's position: same element if it still exists, else the same offset
        var currentId = _quickNavHandler.CurrentNode?.UIARuntimeId;
        int offset = _cursor.TextOffset;

        _quickNavHandler.SetDocument(updated);
        var node = currentId is not null ? updated.FindByRuntimeId(currentId) : null;
        _cursor.SetDocument(updated, node?.TextRange.Start ?? offset);
        if (currentId is not null)
            _quickNavHandler.CurrentNode = node ?? _cursor.CurrentNode;
    }

    public void HandleElementsListClosed(ElementsListClosedEvent evt)
    {
        _modalOpen = false;
        UpdateDocumentActive();

        var selected = evt.SelectedNode;
        var document = _quickNavHandler.CurrentDocument;
        if (selected is null || document is null)
            return;

        // The document may have been updated while the dialog was open
        var node = document.FindByRuntimeId(selected.UIARuntimeId) ?? selected;
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

    private void MoveTo(VBufferNode node)
    {
        _quickNavHandler.CurrentNode = node;
        _cursor?.MoveTo(node.TextRange.Start);
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
        _sayAllCursor = new VBufferCursor(document, _audioCuePlayer);
        _sayAllCursor.MoveTo(_cursor.TextOffset);
        _sayAllController.Start(_sayAllCursor);
    }

    private void StopSayAll()
    {
        var sayAllCursor = _sayAllCursor;
        if (sayAllCursor is null)
            return;

        _sayAllCursor = null;
        _sayAllController.Cancel();

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
        var node = _quickNavHandler.CurrentNode;
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
        Speak(text);
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
