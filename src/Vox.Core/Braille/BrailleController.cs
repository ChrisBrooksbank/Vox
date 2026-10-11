using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Speech;

namespace Vox.Core.Braille;

/// <summary>The connected braille display (or <see cref="NoBrailleDisplay"/>) and its keys.</summary>
public interface IBrailleDisplaySource
{
    /// <summary>The display in use; never null.</summary>
    IBrailleDisplay Current { get; }

    /// <summary>Raised when a display is connected or lost (or another one chosen).</summary>
    event EventHandler? DisplayChanged;

    /// <summary>Raised (on the driver's thread) for keys pressed on the current display.</summary>
    event EventHandler<BrailleKeyEventArgs>? KeyPressed;
}

/// <summary>
/// What the braille display shows, and what its keys do. Lines come in from every thread (the
/// focus and browse cursor on the pipeline thread, the caret and review cursor on the UIA thread);
/// the one shown is the newest the tether allows: with <see cref="BrailleTether.Automatic"/> the
/// focus, caret and browse cursor, and the review cursor after a review command until one of those
/// moves. What Vox says is also shown for a few seconds (a flash message) unless it came with a
/// line of its own. Thread-safe: everything runs under one lock, and display writes are quick.
/// </summary>
public sealed class BrailleController : IDisposable
{
    /// <summary>Speech this close to a line update is that line's speech, not a message.</summary>
    public static readonly TimeSpan MessageQuietPeriod = TimeSpan.FromMilliseconds(150);

    /// <summary>How long after panning past a line's edge the next line is shown from its end.</summary>
    private static readonly TimeSpan PanToEndLifetime = TimeSpan.FromSeconds(1);

    private readonly IBrailleDisplaySource _displays;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly IBrailleRouter _router;
    private readonly IKeyboardOutput _keyboard;
    private readonly ILogger<BrailleController> _logger;
    private readonly TimeProvider _time;
    private readonly Func<string, IBrailleTranslator> _translatorFactory;
    private readonly object _lock = new();
    private readonly BraillePresenter _presenter;
    private readonly BrailleInput _input;
    private readonly IDisposable? _settingsSubscription;

    private IBrailleTranslator _translator;
    private string _table;
    private BrailleViewport _viewport;
    private BrailleKeyMap _keyMap;
    private IBrailleDisplay? _keyMapDisplay;

    private BrailleLine? _focusLine;
    private BrailleLine? _trackedLine; // the caret's or browse cursor's line, after the focus line
    private BrailleLine? _reviewLine;
    private BrailleLine? _shown;
    private bool _reviewActive;
    private BrailleLine? _message;
    private ITimer? _messageTimer;
    private ITimer? _blinkTimer;
    private bool _cursorVisible = true;
    private byte[]? _lastWritten;
    private DateTimeOffset _lastLineUpdate = DateTimeOffset.MinValue;
    private (BrailleLineKind Kind, DateTimeOffset Until)? _showEndOfNext;
    private BrailleTether _tether;
    private BrailleTether _tetherSetting;
    private bool _disposed;

    public BrailleController(IBrailleDisplaySource displays, IOptionsMonitor<VoxSettings> settings, IBrailleRouter router,
        IKeyboardOutput keyboard, ILogger<BrailleController> logger, Func<string, IBrailleTranslator>? translatorFactory = null,
        TimeProvider? time = null, BrailleLabels? labels = null)
    {
        _displays = displays;
        _settings = settings;
        _router = router;
        _keyboard = keyboard;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _translatorFactory = translatorFactory ?? (table => BrailleTranslatorFactory.Create(table, null, logger));
        _presenter = new BraillePresenter(labels);
        _table = settings.CurrentValue.BrailleTable;
        _translator = _translatorFactory(_table);
        _viewport = new BrailleViewport(_translator);
        _keyMap = BrailleKeyMap.ForDriver(null);
        _tether = _tetherSetting = settings.CurrentValue.BrailleTether;
        // Computer braille (no liblouis) is never contracted, whatever the table setting says
        _input = new BrailleInput(() => _translator,
            () => _translator is not ComputerBrailleTranslator && BrailleTables.IsContracted(_table), keyboard);

        _displays.DisplayChanged += OnDisplayChanged;
        _displays.KeyPressed += OnKeyPressed;
        _settingsSubscription = settings.OnChange(OnSettingsChanged);
    }

    public BraillePresenter Presenter => _presenter;

    /// <summary>A display is connected (lines that cost UIA calls to read are only read then).</summary>
    public bool IsActive => _displays.Current.IsConnected;

    /// <summary>What braille follows now (changed by <see cref="CycleTether"/> or the setting).</summary>
    public BrailleTether Tether
    {
        get { lock (_lock) return _tether; }
    }

    /// <summary>The line shown (a message while one is up), or null before the first.</summary>
    public BrailleLine? ShownLine
    {
        get { lock (_lock) return _message ?? _shown; }
    }

    /// <summary>Raised (outside the lock) when the tether changes, with what it is now, so it can be said.</summary>
    public event EventHandler<BrailleTether>? TetherChanged;

    /// <summary>A newly focused object, with its ancestors from the window down (empty until they are known).</summary>
    public void ShowFocus(BrailleObject focus, IReadOnlyList<BrailleObject> ancestors)
    {
        lock (_lock)
        {
            var mode = _settings.CurrentValue.BrailleFocusContext;
            var sameFocus = _focusLine is not null && _focusObject is not null && _focusObject.IsSameObject(focus);
            _focusObject = focus;
            _focusLine = _presenter.ForFocus(focus, ancestors, mode);
            if (!sameFocus)
            {
                _trackedLine = null;
                _input.Discard();
            }
            if (sameFocus && _trackedLine is not null)
                return; // the ancestors arrived after the caret or browse line: keep that line
            _reviewActive = false;
            Update(_focusLine, keepWindow: false);
        }
    }

    private BrailleObject? _focusObject;

    /// <summary>The caret's line in the focused control (from the UIA thread).</summary>
    public void ShowEditLine(string lineText, int caretInLine, (int Start, int End)? selectionInLine)
    {
        lock (_lock)
        {
            var line = BraillePresenter.ForEditLine(lineText, caretInLine, selectionInLine);
            var keep = _trackedLine is { Kind: BrailleLineKind.EditLine } previous && previous.Text.Length > 0
                && ReferenceEquals(_shown, _trackedLine);
            _trackedLine = line;
            _reviewActive = false;
            Update(line, keepWindow: keep);
        }
    }

    /// <summary>The browse cursor's line (from the pipeline thread; built with <see cref="Presenter"/>).</summary>
    public void ShowBrowseLine(BrailleLine line)
    {
        lock (_lock)
        {
            var keep = _trackedLine is { Kind: BrailleLineKind.BrowseLine } previous
                && previous.SourceStart == line.SourceStart && ReferenceEquals(_shown, _trackedLine);
            _trackedLine = line;
            _reviewActive = false;
            Update(line, keepWindow: keep);
        }
    }

    /// <summary>The browse cursor left the page (Focus mode, another window): the focus or caret shows again.</summary>
    public void ClearBrowseLine()
    {
        lock (_lock)
        {
            if (_trackedLine?.Kind != BrailleLineKind.BrowseLine)
                return;
            _trackedLine = null;
            if (_focusLine is not null)
                Update(_focusLine, keepWindow: false);
        }
    }

    /// <summary>The review cursor's line after a review command (from the UIA thread).</summary>
    public void ShowReviewLine(string lineText, int cursorInLine)
    {
        lock (_lock)
        {
            _reviewLine = BraillePresenter.ForReviewLine(lineText, cursorInLine);
            _reviewActive = true;
            Update(_reviewLine, keepWindow: false);
        }
    }

    /// <summary>
    /// Something Vox says: shown as a message for the message timeout unless a line came with it
    /// (the focus, caret or cursor speech). Call as it is queued for speech.
    /// </summary>
    public void OfferMessage(string text)
    {
        if (_settings.CurrentValue.BrailleMessageTimeoutSeconds <= 0 || string.IsNullOrWhiteSpace(text) || !IsActive)
            return;
        var queuedAt = _time.GetUtcNow();
        var message = Flatten(StateSounds.ToWords(text));
        ITimer? timer = null;
        timer = _time.CreateTimer(_ =>
        {
            timer?.Dispose();
            lock (_lock)
            {
                // A line update just before or after: this was that line's speech
                if (_disposed || (_lastLineUpdate - queuedAt).Duration() <= MessageQuietPeriod)
                    return;
                ShowMessage(message);
            }
        }, null, MessageQuietPeriod, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Shows <paramref name="text"/> now, for the message timeout.</summary>
    public void ShowMessage(string text)
    {
        lock (_lock)
        {
            var seconds = _settings.CurrentValue.BrailleMessageTimeoutSeconds;
            if (seconds <= 0)
                return;
            _message = BrailleLine.Message(Flatten(text));
            _viewport.Show(_message, CellCount, ViewOptions());
            RestartMessageTimer(seconds);
            Render();
        }
    }

    public void PanLeft()
    {
        lock (_lock)
        {
            if (_viewport.PanLeft())
            {
                Touched();
                Render();
                return;
            }
            if (_message is not null)
                return;
            MoveLine(-1, toEnd: true);
        }
    }

    public void PanRight()
    {
        lock (_lock)
        {
            if (_viewport.PanRight())
            {
                Touched();
                Render();
                return;
            }
            if (_message is not null)
            {
                DismissMessage();
                return;
            }
            MoveLine(+1, toEnd: false);
        }
    }

    /// <summary>The previous (−1) or next (+1) line of what is shown.</summary>
    public void MoveLine(int direction)
    {
        lock (_lock)
        {
            DismissMessage();
            MoveLine(direction, toEnd: false);
        }
    }

    /// <summary>A routing key over display cell <paramref name="cell"/>.</summary>
    public void Route(int cell)
    {
        lock (_lock)
        {
            if (_message is not null)
            {
                DismissMessage();
                return;
            }
            if (_shown is not { } line || _viewport.TextOffsetAtCell(cell) is not { } offset)
                return;
            switch (line.Kind)
            {
                case BrailleLineKind.Focus:
                    _router.ActivateFocus();
                    break;
                case BrailleLineKind.EditLine:
                    _input.Flush();
                    _router.MoveCaret(line.SourceOffsetAt(offset));
                    break;
                case BrailleLineKind.BrowseLine:
                    var source = line.SourceOffsetAt(offset);
                    // Pressed again where the cursor already is: activate
                    var activate = line.CursorOffset is { } cursor && line.SourceOffsetAt(cursor) == source;
                    _router.RouteBrowse(source, activate);
                    break;
                case BrailleLineKind.Review:
                    _router.MoveReviewCursor(line.SourceOffsetAt(offset));
                    break;
            }
        }
    }

    /// <summary>Switches what braille follows: automatic → focus → review → automatic.</summary>
    public void CycleTether()
    {
        BrailleTether tether;
        lock (_lock)
        {
            _tether = _tether switch
            {
                BrailleTether.Automatic => BrailleTether.Focus,
                BrailleTether.Focus => BrailleTether.Review,
                _ => BrailleTether.Automatic,
            };
            tether = _tether;
            _reviewActive = tether == BrailleTether.Review && _reviewLine is not null;
            if (CurrentLine() is { } line)
                Update(line, keepWindow: false, touch: false);
        }
        TetherChanged?.Invoke(this, tether);
    }

    /// <summary>Text for <paramref name="tether"/> as Vox says it.</summary>
    public static string Describe(BrailleTether tether) => tether switch
    {
        BrailleTether.Focus => "Braille tethered to focus",
        BrailleTether.Review => "Braille tethered to review",
        _ => "Braille tethered automatically",
    };

    private void OnKeyPressed(object? sender, BrailleKeyEventArgs e)
    {
        try
        {
            HandleKey(e);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error handling a braille display key");
        }
    }

    /// <summary>Runs a chord pressed on the display.</summary>
    public void HandleKey(BrailleKeyEventArgs e)
    {
        BrailleKeyMap keyMap;
        lock (_lock)
        {
            EnsureKeyMap();
            keyMap = _keyMap;
        }
        switch (keyMap.Resolve(e))
        {
            case BrailleKeyResult.Typed typed:
                _input.Cell(typed.Cell);
                break;
            case BrailleKeyResult.Character character:
                _input.Flush();
                _keyboard.TypeText(character.Text);
                break;
            case BrailleKeyResult.Bound { Binding: var binding, RoutingCell: var routingCell }:
                Run(binding, routingCell);
                break;
        }
    }

    private void Run(BrailleBinding binding, int? routingCell)
    {
        switch (binding.Action)
        {
            case BrailleAction.PanLeft: PanLeft(); return;
            case BrailleAction.PanRight: PanRight(); return;
            case BrailleAction.Route when routingCell is { } cell: Route(cell); return;
            case BrailleAction.PreviousLine: MoveLine(-1); return;
            case BrailleAction.NextLine: MoveLine(+1); return;
            case BrailleAction.CycleTether: CycleTether(); return;
            case BrailleAction.Space: _input.Space(); return;
            case BrailleAction.Backspace: _input.Backspace(); return;
            case BrailleAction.Enter: _input.Enter(); return;
        }
        lock (_lock) DismissMessage();
        if (binding.Command is { } command)
        {
            _input.Flush();
            _router.RunCommand(command);
        }
        else if (binding.Key is { } key)
        {
            _input.Flush();
            _keyboard.SendKey(key);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Under the lock
    // ---------------------------------------------------------------------------------------

    /// <summary>The line the tether shows now, ignoring messages.</summary>
    private BrailleLine? CurrentLine() => _tether switch
    {
        BrailleTether.Review => _reviewLine ?? _trackedLine ?? _focusLine,
        BrailleTether.Focus => _trackedLine ?? _focusLine,
        _ => _reviewActive && _reviewLine is not null ? _reviewLine : _trackedLine ?? _focusLine,
    };

    /// <summary>A line changed: shows the line the tether picks now (it may not be this one).</summary>
    private void Update(BrailleLine changed, bool keepWindow, bool touch = true)
    {
        if (touch)
            _lastLineUpdate = _time.GetUtcNow();
        var line = CurrentLine();
        if (line is null)
            return;
        // A line shown again (the tether moved back to it) keeps nothing of the old window
        var sameLine = ReferenceEquals(line, changed) && keepWindow;
        var showEnd = _showEndOfNext is { } pending && pending.Kind == line.Kind && pending.Until >= _time.GetUtcNow()
            && ReferenceEquals(line, changed) && !ReferenceEquals(line, _shown);
        if (showEnd)
            _showEndOfNext = null;
        _shown = line;
        DismissMessage(render: false);
        _viewport.Show(line, CellCount, ViewOptions(), sameLine);
        if (showEnd)
            _viewport.ShowLineEnd();
        _cursorVisible = true;
        RestartBlink();
        Render();
    }

    private void MoveLine(int direction, bool toEnd)
    {
        if (_shown is not { } line)
            return;
        if (toEnd)
            _showEndOfNext = (line.Kind, _time.GetUtcNow() + PanToEndLifetime);
        switch (line.Kind)
        {
            case BrailleLineKind.BrowseLine:
                _router.RunCommand(direction < 0 ? NavigationCommand.PrevLine : NavigationCommand.NextLine);
                break;
            case BrailleLineKind.EditLine:
                _input.Flush();
                _keyboard.SendKey(new OutputKey(direction < 0 ? 0x26 : 0x28));
                break;
            case BrailleLineKind.Review:
                _router.RunCommand(direction < 0 ? NavigationCommand.ReviewPrevLine : NavigationCommand.ReviewNextLine);
                break;
            default:
                _showEndOfNext = null;
                break;
        }
    }

    /// <summary>A pan or key on a message keeps it up a while longer.</summary>
    private void Touched()
    {
        if (_message is not null)
            RestartMessageTimer(_settings.CurrentValue.BrailleMessageTimeoutSeconds);
    }

    private void RestartMessageTimer(int seconds)
    {
        _messageTimer?.Dispose();
        _messageTimer = _time.CreateTimer(_ =>
        {
            lock (_lock)
            {
                if (!_disposed)
                    DismissMessage();
            }
        }, null, TimeSpan.FromSeconds(Math.Max(seconds, 1)), Timeout.InfiniteTimeSpan);
    }

    private void DismissMessage(bool render = true)
    {
        if (_message is null)
            return;
        _message = null;
        _messageTimer?.Dispose();
        _messageTimer = null;
        if (!render)
            return;
        if (_shown is not null)
            _viewport.Show(_shown, CellCount, ViewOptions(), keepWindow: false);
        Render();
    }

    private void RestartBlink()
    {
        _blinkTimer?.Dispose();
        _blinkTimer = null;
        var settings = _settings.CurrentValue;
        if (!settings.BrailleCursorBlink || _viewport.Line?.CursorOffset is null || CellCount == 0)
            return;
        var period = TimeSpan.FromMilliseconds(Math.Clamp(settings.BrailleCursorBlinkRateMs, 100, 5000));
        _blinkTimer = _time.CreateTimer(_ =>
        {
            lock (_lock)
            {
                if (_disposed || _message is not null)
                    return;
                _cursorVisible = !_cursorVisible;
                Render();
            }
        }, null, period, period);
    }

    private void Render()
    {
        var display = _displays.Current;
        if (!display.IsConnected || display.CellCount == 0)
            return;
        if (_viewport.CellCount != display.CellCount)
            _viewport.Resize(display.CellCount);
        var cells = _viewport.Render(_cursorVisible && _message is null);
        if (_lastWritten is not null && cells.AsSpan().SequenceEqual(_lastWritten))
            return;
        _lastWritten = cells;
        try
        {
            display.Write(cells, _message is null ? _viewport.CursorCellInWindow : null);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Braille display write failed");
        }
    }

    private int CellCount => _displays.Current.CellCount;

    private BrailleViewOptions ViewOptions() => BrailleViewOptions.From(_settings.CurrentValue);

    private void EnsureKeyMap()
    {
        var display = _displays.Current;
        if (ReferenceEquals(display, _keyMapDisplay))
            return;
        _keyMapDisplay = display;
        _keyMap = BrailleKeyMap.ForDriver((display as IBrailleDisplayDriver)?.Id);
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            _lastWritten = null;
            EnsureKeyMap();
            if ((_message ?? _shown) is { } line)
                _viewport.Show(line, CellCount, ViewOptions(), keepWindow: false);
            RestartBlink();
            Render();
        }
    }

    private void OnSettingsChanged(VoxSettings settings, string? _)
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            if (settings.BrailleTable != _table)
            {
                _table = settings.BrailleTable;
                _translator = _translatorFactory(_table);
                _viewport = new BrailleViewport(_translator);
            }
            // The tether cycled from the display stays until the setting itself changes
            if (settings.BrailleTether != _tetherSetting)
                _tether = _tetherSetting = settings.BrailleTether;
            _lastWritten = null;
            if ((_message ?? CurrentLine()) is { } line)
            {
                if (_message is null)
                    _shown = line;
                _viewport.Show(line, CellCount, ViewOptions(), keepWindow: true);
            }
            RestartBlink();
            Render();
        }
    }

    /// <summary>One line: line breaks and tabs as spaces.</summary>
    private static string Flatten(string text) =>
        text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _messageTimer?.Dispose();
            _blinkTimer?.Dispose();
        }
        _displays.DisplayChanged -= OnDisplayChanged;
        _displays.KeyPressed -= OnKeyPressed;
        _settingsSubscription?.Dispose();
    }
}
