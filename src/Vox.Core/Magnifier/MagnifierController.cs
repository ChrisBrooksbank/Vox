using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Localization;
using Vox.Core.Ocr;

namespace Vox.Core.Magnifier;

/// <summary>The first-run wizard's offer of the magnifier ("Do you use magnification?").</summary>
public interface IMagnifierOffer
{
    /// <summary>How the key that turns the magnifier on and off is said.</summary>
    string ToggleKey { get; }

    /// <summary>Turns the magnifier on; null when it is on, else what to say about why it isn't.</summary>
    string? TurnOn();
}

/// <summary>What the magnifier can follow, each with a setting.</summary>
public enum MagnifierTrackingSource
{
    Focus,
    Caret,
    BrowseCursor,
    ReviewCursor,
    Mouse,
}

/// <summary>
/// Vox's magnifier: turns it on and off, zooms, pans, changes the view and the colour filter
/// (the magnifier commands), and moves the view to what it follows — the focus, the caret, the
/// browse cursor's element, the review cursor and the mouse pointer, each when its setting says.
/// A keyboard-driven move wins over the mouse for <see cref="MouseHoldOff"/>, so a hand resting
/// on the mouse can't pull the view away from where the keyboard just took it. The view glides
/// there (<see cref="VoxSettings.MagnifierSmoothTracking"/>) or jumps. Its settings are ordinary
/// settings, so a configuration profile can turn it on or zoom in for one application.
/// The screen curtain wins over it (<see cref="ScreenCurtainActive"/>). Never on the secure
/// screens or with the DisableMagnifier policy. Thread-safe (one lock); the clock and timers come
/// from an injectable <see cref="TimeProvider"/>.
/// </summary>
public sealed class MagnifierController : IMagnifierOffer, IDisposable
{
    /// <summary>How long after a keyboard-driven move the mouse pointer is not followed.</summary>
    public static readonly TimeSpan MouseHoldOff = TimeSpan.FromSeconds(1);

    /// <summary>How long a smooth move takes.</summary>
    public static readonly TimeSpan GlideDuration = TimeSpan.FromMilliseconds(200);

    /// <summary>How often a smooth move redraws.</summary>
    public static readonly TimeSpan GlideFrame = TimeSpan.FromMilliseconds(16);

    private readonly object _lock = new();
    private readonly IMagnifier _magnifier;
    private readonly IDisplayLayout _displays;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly Action<VoxSettings> _updateSettings;
    private readonly Action<string> _say;
    private readonly IAudioCuePlayer? _cues;
    private readonly RunPolicy _policy;
    private readonly ILogger<MagnifierController> _logger;
    private readonly TimeProvider _time;
    private IDisposable? _settingsSubscription;

    // Guarded by _lock
    private bool _on;
    private bool _curtain;
    private bool _disposed;
    private MagnifierViewport? _viewport;
    private (int X, int Y)? _point;
    private DateTimeOffset _lastKeyboardMove = DateTimeOffset.MinValue;
    private ITimer? _glideTimer;
    private (double Left, double Top) _glideFrom;
    private (double Left, double Top) _glideTo;
    private DateTimeOffset _glideStart;
    // The settings the view was last drawn with, to tell what a settings change altered
    private VoxSettings? _shownWith;
    private MagnifierFrame? _lastFrame;
    // MagnifierEnabled as last seen, so only a change to it turns the magnifier on or off
    private bool _enabledSetting;

    public MagnifierController(IMagnifier magnifier, IDisplayLayout displays, IOptionsMonitor<VoxSettings> settings,
        Action<VoxSettings> updateSettings, Action<string> say, ILogger<MagnifierController> logger,
        RunPolicy? policy = null, IAudioCuePlayer? cues = null, TimeProvider? time = null)
    {
        _magnifier = magnifier;
        _displays = displays;
        _settings = settings;
        _updateSettings = updateSettings;
        _say = say;
        _logger = logger;
        _policy = policy ?? RunPolicy.Normal;
        _cues = cues;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Whether the magnifier may be used here (not on the secure screens, not with the DisableMagnifier policy).</summary>
    public bool IsAvailable => _policy.AllowMagnifier;

    /// <summary>The magnifier is magnifying now.</summary>
    public bool IsOn
    {
        get { lock (_lock) return _on; }
    }

    /// <summary>What the magnifier shows now (null while off), for tests and diagnostics.</summary>
    public MagnifierViewport? Viewport
    {
        get { lock (_lock) return _on ? _viewport : null; }
    }

    /// <summary>
    /// Follows the settings, and turns the magnifier on when <see cref="VoxSettings.MagnifierEnabled"/>
    /// says (it says so only when it can't).
    /// </summary>
    public void Start()
    {
        if (!IsAvailable)
            return;
        _settingsSubscription ??= _settings.OnChange((settings, _) => OnSettingsChanged(settings));
        lock (_lock)
            _enabledSetting = _settings.CurrentValue.MagnifierEnabled;
        if (_settings.CurrentValue.MagnifierEnabled)
            TurnOn(announce: false);
    }

    /// <summary>
    /// The screen curtain is on (or off again). It wins over the magnifier: the magnifier stops,
    /// saying so, and comes back when the curtain goes if its setting still says on.
    /// </summary>
    public bool ScreenCurtainActive
    {
        get { lock (_lock) return _curtain; }
        set
        {
            bool stopped;
            lock (_lock)
            {
                if (_curtain == value || _disposed)
                    return;
                _curtain = value;
                stopped = value && _on;
                if (stopped)
                    StopLocked();
            }
            if (stopped)
                _say(VoxStrings.Get("Magnifier.CurtainWins"));
            else if (!value && _settings.CurrentValue.MagnifierEnabled && IsAvailable)
                TurnOn(announce: false);
        }
    }

    /// <summary>Runs <paramref name="command"/> if it is a magnifier command; returns whether it was.</summary>
    public bool TryHandle(NavigationCommand command)
    {
        if (!IsMagnifierCommand(command))
            return false;
        if (!IsAvailable)
        {
            _say(NotAvailableText);
            return true;
        }
        switch (command)
        {
            case NavigationCommand.ToggleMagnifier: Toggle(); return true;
            case NavigationCommand.MagnifierZoomIn: ZoomBy(+1); return true;
            case NavigationCommand.MagnifierZoomOut: ZoomBy(-1); return true;
            case NavigationCommand.MagnifierNextFilter: NextFilter(); return true;
            case NavigationCommand.MagnifierNextView: NextView(); return true;
            case NavigationCommand.MagnifierMoveView: MoveView(); return true;
            case NavigationCommand.MagnifierViewLarger: ResizeView(+1); return true;
            case NavigationCommand.MagnifierViewSmaller: ResizeView(-1); return true;
            case NavigationCommand.MagnifierPanLeft: Pan(PanDirection.Left); return true;
            case NavigationCommand.MagnifierPanRight: Pan(PanDirection.Right); return true;
            case NavigationCommand.MagnifierPanUp: Pan(PanDirection.Up); return true;
            case NavigationCommand.MagnifierPanDown: Pan(PanDirection.Down); return true;
            default: return false;
        }
    }

    private static bool IsMagnifierCommand(NavigationCommand command) => command is NavigationCommand.ToggleMagnifier
        or NavigationCommand.MagnifierZoomIn or NavigationCommand.MagnifierZoomOut or NavigationCommand.MagnifierNextFilter
        or NavigationCommand.MagnifierNextView or NavigationCommand.MagnifierMoveView or NavigationCommand.MagnifierViewLarger
        or NavigationCommand.MagnifierViewSmaller or NavigationCommand.MagnifierPanLeft or NavigationCommand.MagnifierPanRight
        or NavigationCommand.MagnifierPanUp or NavigationCommand.MagnifierPanDown;

    /// <summary>Why the magnifier can't be used here.</summary>
    private string NotAvailableText => VoxStrings.Get(_policy.IsSecure ? "Magnifier.NotOnSecureScreens" : "Magnifier.OffByPolicy");

    // -------------------------------------------------------------------------
    // What the magnifier follows
    // -------------------------------------------------------------------------

    /// <summary>The focus moved to an object with these bounds (null: it has none).</summary>
    public void FocusMoved(ScreenRect? bounds) => Follow(MagnifierTrackingSource.Focus, bounds);

    /// <summary>The caret moved; these are its bounds.</summary>
    public void CaretMoved(ScreenRect? bounds) => Follow(MagnifierTrackingSource.Caret, bounds);

    /// <summary>The browse cursor moved to an element with these bounds.</summary>
    public void BrowseCursorMoved(ScreenRect? bounds) => Follow(MagnifierTrackingSource.BrowseCursor, bounds);

    /// <summary>The review cursor moved to text with these bounds.</summary>
    public void ReviewCursorMoved(ScreenRect? bounds) => Follow(MagnifierTrackingSource.ReviewCursor, bounds);

    /// <summary>The mouse pointer moved to (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public void MouseMoved(int x, int y) => Follow(MagnifierTrackingSource.Mouse, new ScreenRect(x, y, 1, 1));

    /// <summary>Whether <paramref name="source"/> is followed now: the magnifier is on and the source's setting says so.</summary>
    public bool Follows(MagnifierTrackingSource source)
    {
        lock (_lock)
        {
            if (!_on)
                return false;
        }
        return IsFollowed(_settings.CurrentValue, source);
    }

    private static bool IsFollowed(VoxSettings settings, MagnifierTrackingSource source) => source switch
    {
        MagnifierTrackingSource.Focus => settings.MagnifierFollowFocus,
        MagnifierTrackingSource.Caret => settings.MagnifierFollowCaret,
        MagnifierTrackingSource.BrowseCursor => settings.MagnifierFollowBrowseCursor,
        MagnifierTrackingSource.ReviewCursor => settings.MagnifierFollowReviewCursor,
        _ => settings.MagnifierFollowMouse,
    };

    private void Follow(MagnifierTrackingSource source, ScreenRect? bounds)
    {
        if (bounds is not { IsEmpty: false } target)
            return;
        var settings = _settings.CurrentValue;
        if (!IsFollowed(settings, source))
            return;
        lock (_lock)
        {
            if (!_on || _viewport is not { } viewport)
            {
                // Where the keyboard is, for the view to start around when the magnifier comes on
                if (source != MagnifierTrackingSource.Mouse)
                    _point = target.Center;
                return;
            }
            var now = _time.GetUtcNow();
            if (source == MagnifierTrackingSource.Mouse)
            {
                if (now - _lastKeyboardMove < MouseHoldOff)
                    return;
            }
            else
                _lastKeyboardMove = now;

            _point = target.Center;
            viewport = Refit(viewport, settings);
            MagnifierViewport moved;
            if (settings.MagnifierView == MagnifierView.Lens)
                // The lens sits over what it follows
                moved = viewport.CentreOnPoint(target.Left + target.Width / 2.0, target.Top + target.Height / 2.0);
            else if (source is MagnifierTrackingSource.Caret or MagnifierTrackingSource.Mouse)
                // Typing and pointing move the view only as far as they must
                moved = viewport.KeepInView(target, Margin(viewport));
            else
                moved = viewport.Shows(target) ? viewport : viewport.CentreOn(target);
            MoveTo(viewport, moved, settings);
        }
    }

    /// <summary>The room kept around the caret and the pointer: a tenth of the view.</summary>
    private static int Margin(MagnifierViewport viewport) =>
        (int)(Math.Min(viewport.SourceWidth, viewport.SourceHeight) / 10);

    // -------------------------------------------------------------------------
    // Commands
    // -------------------------------------------------------------------------

    private void Toggle()
    {
        if (IsOn)
        {
            TurnOff();
            Persist(s => s with { MagnifierEnabled = false });
            _say(VoxStrings.Get("Magnifier.Off"));
        }
        else if (TurnOn(announce: true))
            Persist(s => s with { MagnifierEnabled = true });
    }

    private void ZoomBy(int direction)
    {
        var settings = _settings.CurrentValue;
        if (!IsOn)
        {
            // Zooming in starts the magnifier at the zoom it had, as Windows+Plus does
            if (direction > 0 && TurnOn(announce: true))
                Persist(s => s with { MagnifierEnabled = true });
            else if (direction < 0)
                SayOff();
            return;
        }
        int current = MagnifierZoom.Clamp(settings.MagnifierZoomPercent);
        if ((direction > 0 ? MagnifierZoom.In(current) : MagnifierZoom.Out(current)) is not { } zoom)
        {
            _cues?.Play("boundary");
            return;
        }
        Persist(s => s with { MagnifierZoomPercent = zoom });
        Refresh(_settings.CurrentValue);
        _say(MagnifierZoom.Describe(zoom));
    }

    private void NextFilter()
    {
        if (ScreenCurtainActive)
        {
            _say(VoxStrings.Get("Magnifier.CurtainOn"));
            return;
        }
        var filters = Enum.GetValues<MagnifierColorFilter>();
        var next = filters[(Array.IndexOf(filters, _settings.CurrentValue.MagnifierColorFilter) + 1) % filters.Length];
        Persist(s => s with { MagnifierColorFilter = next });
        Refresh(_settings.CurrentValue);
        _say(IsOn ? SettingsPages.ChoiceName(next) : VoxStrings.Format("Magnifier.SetWhileOff", SettingsPages.ChoiceName(next)));
    }

    private void NextView()
    {
        var views = Enum.GetValues<MagnifierView>();
        var next = views[(Array.IndexOf(views, _settings.CurrentValue.MagnifierView) + 1) % views.Length];
        Persist(s => s with { MagnifierView = next });
        Refresh(_settings.CurrentValue);
        _say(IsOn ? SettingsPages.ChoiceName(next) : VoxStrings.Format("Magnifier.SetWhileOff", SettingsPages.ChoiceName(next)));
    }

    /// <summary>Moves the docked view to the next edge of the screen.</summary>
    private void MoveView()
    {
        var settings = _settings.CurrentValue;
        if (settings.MagnifierView != MagnifierView.Docked)
        {
            _say(VoxStrings.Get("Magnifier.OnlyDockedMoves"));
            return;
        }
        var edge = MagnifierLayout.NextEdge(settings.MagnifierDockEdge);
        Persist(s => s with { MagnifierDockEdge = edge });
        Refresh(_settings.CurrentValue);
        _say(SettingsPages.ChoiceName(edge));
    }

    /// <summary>Makes the docked view deeper or shallower, or the lens larger or smaller.</summary>
    private void ResizeView(int direction)
    {
        var settings = _settings.CurrentValue;
        int step = direction * MagnifierLayout.SizeStepPercent;
        int current, size;
        Func<VoxSettings, int, VoxSettings> set;
        switch (settings.MagnifierView)
        {
            case MagnifierView.Docked:
                current = settings.MagnifierDockedSizePercent;
                set = (s, v) => s with { MagnifierDockedSizePercent = v };
                break;
            case MagnifierView.Lens:
                current = settings.MagnifierLensSizePercent;
                set = (s, v) => s with { MagnifierLensSizePercent = v };
                break;
            default:
                _say(VoxStrings.Get("Magnifier.FullScreenSize"));
                return;
        }
        size = MagnifierLayout.ClampSize(MagnifierLayout.ClampSize(current) + step);
        if (size == MagnifierLayout.ClampSize(current))
        {
            _cues?.Play("boundary");
            return;
        }
        Persist(s => set(s, size));
        Refresh(_settings.CurrentValue);
        _say(VoxStrings.Format("Magnifier.Size", size));
    }

    private void Pan(PanDirection direction)
    {
        var settings = _settings.CurrentValue;
        bool atEdge = false;
        lock (_lock)
        {
            if (!_on || _viewport is not { } viewport)
            {
                atEdge = true;
            }
            else
            {
                viewport = Refit(viewport, settings);
                // Pan from where a glide is going, so quick presses add up
                var from = _glideTimer is not null ? viewport.WithOrigin(_glideTo.Left, _glideTo.Top) : viewport;
                if (from.PanStep(direction) is { } moved)
                {
                    _lastKeyboardMove = _time.GetUtcNow();
                    _point = ((int)moved.Centre.X, (int)moved.Centre.Y);
                    MoveTo(viewport, moved, settings);
                }
                else
                    atEdge = true;
            }
        }
        if (!IsOn)
            SayOff();
        else if (atEdge)
            _cues?.Play("boundary");
    }

    private void SayOff() => _say(VoxStrings.Get("Magnifier.Off"));

    // -------------------------------------------------------------------------
    // On, off and drawing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Turns the magnifier on, at the settings' zoom, around what the keyboard was last on (else
    /// the middle of the primary display); says why when it can't. <paramref name="announce"/>:
    /// say the zoom when it starts (and that it isn't available here, which is otherwise silent).
    /// </summary>
    public bool TurnOn(bool announce)
    {
        if (!IsAvailable)
        {
            if (announce)
                _say(NotAvailableText);
            return false;
        }
        if (Open() is { } refusal)
        {
            _say(refusal);
            return false;
        }
        if (announce)
            _say(VoxStrings.Format("Magnifier.On", MagnifierZoom.Describe(_settings.CurrentValue.MagnifierZoomPercent)));
        return true;
    }

    /// <summary>Starts magnifying; null when it is on, else what to say about why it isn't.</summary>
    public string? Open()
    {
        if (!IsAvailable)
            return NotAvailableText;
        var settings = _settings.CurrentValue;
        string? refusal = null;
        lock (_lock)
        {
            if (_disposed)
                return VoxStrings.Get("Magnifier.Unavailable");
            if (_on)
                return null;
            if (_curtain)
                refusal = VoxStrings.Get("Magnifier.CurtainOn");
            else
            {
                MagnifierStartResult result;
                try
                {
                    result = _magnifier.Start();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "The magnifier could not start");
                    result = MagnifierStartResult.Unavailable;
                }
                switch (result)
                {
                    case MagnifierStartResult.Started:
                        _on = true;
                        var displays = Displays();
                        var around = _point is { } point ? new ScreenRect(point.X, point.Y, 1, 1) : displays[0];
                        var (width, height) = ViewSize(settings, displays);
                        _viewport = MagnifierViewport.Create(MagnifierLayout.VirtualScreen(displays), width, height,
                            MagnifierZoom.Factor(settings.MagnifierZoomPercent), around);
                        ShowLocked(_viewport, settings);
                        break;
                    case MagnifierStartResult.WindowsMagnifierRunning:
                        refusal = VoxStrings.Get("Magnifier.WindowsMagnifierRunning");
                        break;
                    default:
                        refusal = VoxStrings.Get("Magnifier.Unavailable");
                        break;
                }
            }
        }
        if (refusal is not null)
            _logger.LogInformation("The magnifier did not start: {Reason}", refusal);
        return refusal;
    }

    // The first-run wizard's question: the key comes from the keymap in use (set by the host)
    string IMagnifierOffer.ToggleKey => ToggleKeyName?.Invoke() ?? string.Empty;

    string? IMagnifierOffer.TurnOn() => Open();

    /// <summary>How the key that turns the magnifier on and off is said (set by the host from the keymap).</summary>
    public Func<string>? ToggleKeyName { get; set; }

    /// <summary>Stops magnifying (the setting is left as it is).</summary>
    public void TurnOff()
    {
        lock (_lock)
            StopLocked();
    }

    private void StopLocked()
    {
        StopGlide();
        if (!_on)
            return;
        _on = false;
        _shownWith = null;
        _lastFrame = null;
        try
        {
            _magnifier.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The magnifier did not stop cleanly");
        }
    }

    private void OnSettingsChanged(VoxSettings settings)
    {
        bool changed;
        lock (_lock)
        {
            changed = settings.MagnifierEnabled != _enabledSetting;
            _enabledSetting = settings.MagnifierEnabled;
        }
        // A profile, the settings dialog or the wizard turned it on or off (another setting
        // changing doesn't retry a start that was refused, such as beside Windows Magnifier)
        if (changed && settings.MagnifierEnabled && !IsOn && !ScreenCurtainActive)
            TurnOn(announce: false);
        else if (changed && !settings.MagnifierEnabled && IsOn)
            TurnOff();
        else
            Refresh(settings);
    }

    /// <summary>Draws the view again with <paramref name="settings"/> (zoom, view, size, filter).</summary>
    private void Refresh(VoxSettings settings)
    {
        lock (_lock)
        {
            if (!_on || _viewport is not { } viewport)
                return;
            if (_shownWith is { } shown && SameView(shown, settings))
                return;
            StopGlide();
            _viewport = Refit(viewport, settings);
            if (settings.MagnifierView == MagnifierView.Lens && _point is { } point)
                _viewport = _viewport.CentreOnPoint(point.X, point.Y);
            ShowLocked(_viewport, settings);
        }
    }

    private static bool SameView(VoxSettings a, VoxSettings b) =>
        a.MagnifierZoomPercent == b.MagnifierZoomPercent && a.MagnifierView == b.MagnifierView
        && a.MagnifierDockEdge == b.MagnifierDockEdge && a.MagnifierDockedSizePercent == b.MagnifierDockedSizePercent
        && a.MagnifierLensSizePercent == b.MagnifierLensSizePercent && a.MagnifierColorFilter == b.MagnifierColorFilter
        && a.MagnifierSmoothing == b.MagnifierSmoothing;

    /// <summary><paramref name="viewport"/> at the settings' zoom and view size, over the monitors there are now.</summary>
    private MagnifierViewport Refit(MagnifierViewport viewport, VoxSettings settings)
    {
        var displays = Displays();
        var (width, height) = ViewSize(settings, displays);
        var virtualScreen = MagnifierLayout.VirtualScreen(displays);
        double zoom = MagnifierZoom.Factor(settings.MagnifierZoomPercent);
        if (viewport.VirtualScreen != virtualScreen || viewport.ViewWidth != width || viewport.ViewHeight != height)
            viewport = viewport.WithView(virtualScreen, width, height);
        return viewport.Zoom == zoom ? viewport : viewport.WithZoom(zoom);
    }

    /// <summary>The size of the view: the primary display (full screen), else its window.</summary>
    private (int Width, int Height) ViewSize(VoxSettings settings, IReadOnlyList<ScreenRect> displays)
    {
        var window = Window(settings, displays, _point);
        return window is { } rect ? (rect.Width, rect.Height) : (displays[0].Width, displays[0].Height);
    }

    /// <summary>The docked or lens view's window (null for full screen).</summary>
    private static ScreenRect? Window(VoxSettings settings, IReadOnlyList<ScreenRect> displays, (int X, int Y)? point)
    {
        switch (settings.MagnifierView)
        {
            case MagnifierView.Docked:
                // The display the user is working on
                var dockOn = point is { } p ? MagnifierLayout.DisplayAt(displays, p.X, p.Y) : displays[0];
                return MagnifierLayout.Docked(dockOn, settings.MagnifierDockEdge, settings.MagnifierDockedSizePercent);
            case MagnifierView.Lens:
                var (x, y) = point ?? displays[0].Center;
                return MagnifierLayout.Lens(MagnifierLayout.DisplayAt(displays, x, y), x, y, settings.MagnifierLensSizePercent);
            default:
                return null;
        }
    }

    private IReadOnlyList<ScreenRect> Displays()
    {
        IReadOnlyList<ScreenRect> displays;
        try
        {
            displays = _displays.Displays;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the monitors");
            displays = [];
        }
        var usable = displays.Where(d => !d.IsEmpty).ToList();
        return usable.Count > 0 ? usable : [new ScreenRect(0, 0, 1920, 1080)];
    }

    /// <summary>Moves from <paramref name="from"/> to <paramref name="to"/>: a glide, or at once.</summary>
    private void MoveTo(MagnifierViewport from, MagnifierViewport to, VoxSettings settings)
    {
        bool moves = from.Left != to.Left || from.Top != to.Top;
        if (!moves && _glideTimer is null)
        {
            // Nothing to move, but a lens window may still have to be where the point is
            _viewport = to;
            ShowLocked(to, settings);
            return;
        }
        if (!settings.MagnifierSmoothTracking)
        {
            StopGlide();
            _viewport = to;
            ShowLocked(to, settings);
            return;
        }
        _glideFrom = (from.Left, from.Top);
        _glideTo = (to.Left, to.Top);
        _glideStart = _time.GetUtcNow();
        _viewport = from;
        _glideTimer ??= _time.CreateTimer(_ => GlideStep(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _glideTimer.Change(GlideFrame, GlideFrame);
    }

    private void GlideStep()
    {
        lock (_lock)
        {
            if (!_on || _glideTimer is null || _viewport is not { } viewport)
                return;
            double t = Math.Clamp((_time.GetUtcNow() - _glideStart) / GlideDuration, 0, 1);
            // Ease out: fast at first, settling gently
            double eased = 1 - Math.Pow(1 - t, 3);
            var next = viewport.WithOrigin(
                _glideFrom.Left + (_glideTo.Left - _glideFrom.Left) * eased,
                _glideFrom.Top + (_glideTo.Top - _glideFrom.Top) * eased);
            if (t >= 1)
                StopGlide();
            _viewport = next;
            ShowLocked(next, _settings.CurrentValue);
        }
    }

    private void StopGlide()
    {
        _glideTimer?.Dispose();
        _glideTimer = null;
    }

    private void ShowLocked(MagnifierViewport viewport, VoxSettings settings)
    {
        var displays = Displays();
        // The lens's window goes where the view's middle is
        var point = settings.MagnifierView == MagnifierView.Lens
            ? ((int)Math.Round(viewport.Centre.X), (int)Math.Round(viewport.Centre.Y))
            : _point;
        var window = Window(settings, displays, point);
        double left = viewport.Left, top = viewport.Top;
        // Full screen at 1×: the screen as it is, not shifted to wherever the view last was
        if (window is null && viewport.Zoom <= MagnifierViewport.MinZoom)
            (left, top) = (displays[0].Left, displays[0].Top);
        var frame = new MagnifierFrame(settings.MagnifierView, viewport.Zoom, left, top, window,
            settings.MagnifierColorFilter, settings.MagnifierSmoothing);
        _shownWith = settings;
        if (frame == _lastFrame)
            return;
        try
        {
            _magnifier.Show(frame);
            _lastFrame = frame;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "The magnifier could not show its view");
        }
    }

    private void Persist(Func<VoxSettings, VoxSettings> change)
    {
        try
        {
            _updateSettings(change(_settings.CurrentValue));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save the magnifier settings");
        }
    }

    public void Dispose()
    {
        _settingsSubscription?.Dispose();
        _settingsSubscription = null;
        lock (_lock)
        {
            StopLocked();
            _disposed = true;
        }
        _magnifier.Dispose();
    }
}
