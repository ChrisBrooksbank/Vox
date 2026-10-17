using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Localization;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.Touch;

/// <summary>Activates the element at a screen point (UIA thread).</summary>
public interface ITouchActivator
{
    bool ActivateAt(int x, int y);
}

/// <summary>Where keyboard focus is for touch: in a web document, and whether that document is in browse mode.</summary>
public readonly record struct TouchContext(bool InDocument, bool Browsing);

/// <summary>
/// Touch exploration and gestures (design: docs/technical/touch.md). Points from an
/// <see cref="ITouchSource"/> go through a <see cref="GestureRecognizer"/>; an explore point is
/// looked up on the UIA thread and said when it differs from the last one (newest point wins, one
/// lookup at a time); any other gesture is turned into a command by the <see cref="TouchKeyMap"/>
/// and posted to the pipeline like a key. The touch commands themselves are run by
/// <see cref="TryHandle"/>. Thread-safe: points arrive on the source's thread.
/// </summary>
public sealed class TouchController : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(40);

    private static readonly (string Name, NavigationCommand Next, NavigationCommand Previous)[] Rotor =
    [
        ("Touch.Rotor.Headings", NavigationCommand.NextHeading, NavigationCommand.PrevHeading),
        ("Touch.Rotor.Links", NavigationCommand.NextLink, NavigationCommand.PrevLink),
        ("Touch.Rotor.FormFields", NavigationCommand.NextFormField, NavigationCommand.PrevFormField),
        ("Touch.Rotor.Landmarks", NavigationCommand.NextLandmark, NavigationCommand.PrevLandmark),
        ("Touch.Rotor.Lines", NavigationCommand.NextLine, NavigationCommand.PrevLine),
    ];

    private readonly ITouchSource _source;
    private readonly IPointerTargetSource _targets;
    private readonly ITouchActivator _activator;
    private readonly UIAThread _uiaThread;
    private readonly SpeechQueue _speechQueue;
    private readonly AnnouncementBuilder _announcementBuilder;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly Action<VoxSettings> _updateSettings;
    private readonly IEventSink _sink;
    private readonly RunPolicy _policy;
    private readonly Func<TouchContext> _context;
    private readonly ILogger<TouchController> _logger;
    private readonly TimeProvider _time;
    private readonly string? _userKeyMapPath;
    private readonly object _lock = new();
    private readonly GestureRecognizer _recognizer;
    private readonly CancellationTokenSource _stop = new();

    private TouchKeyMap _keyMap;
    private bool _running;
    private Task? _ticker;
    private int _rotor;
    // The point to look up next while a lookup is in flight (newest wins)
    private (int X, int Y)? _pending;
    private bool _lookupInFlight;
    private (int X, int Y)? _lastExplored;
    private string? _lastSpoken;
    private int _disposed;

    public TouchController(ITouchSource source, IPointerTargetSource targets, ITouchActivator activator, UIAThread uiaThread,
        SpeechQueue speechQueue, AnnouncementBuilder announcementBuilder, IOptionsMonitor<VoxSettings> settings,
        Action<VoxSettings> updateSettings, IEventSink sink, RunPolicy policy, Func<TouchContext> context,
        ILogger<TouchController> logger, string? userKeyMapPath = null, TimeProvider? time = null,
        TouchThresholds? thresholds = null, double pixelsPerMm = 96 / 25.4)
    {
        _source = source;
        _targets = targets;
        _activator = activator;
        _uiaThread = uiaThread;
        _speechQueue = speechQueue;
        _announcementBuilder = announcementBuilder;
        _settings = settings;
        _updateSettings = updateSettings;
        _sink = sink;
        _policy = policy;
        _context = context;
        _logger = logger;
        _userKeyMapPath = userKeyMapPath;
        _time = time ?? TimeProvider.System;
        _recognizer = new GestureRecognizer(thresholds, pixelsPerMm);
        _recognizer.Recognized += OnGesture;
        _keyMap = LoadKeyMap();
        _source.PointReceived += OnPoint;
    }

    /// <summary>Whether touch is capturing the screen now.</summary>
    public bool IsRunning
    {
        get { lock (_lock) return _running; }
    }

    /// <summary>Completes when the explore lookup in progress (if any) has finished; for tests.</summary>
    public Task ExploreIdle { get; private set; } = Task.CompletedTask;

    /// <summary>Reloads the gesture bindings (the user's file changed).</summary>
    public void ReloadKeyMap()
    {
        var map = LoadKeyMap();
        lock (_lock) _keyMap = map;
    }

    /// <summary>
    /// Starts or stops capturing so that it matches the TouchEnabled setting and the policy.
    /// Call at startup and whenever settings change.
    /// </summary>
    public void Refresh()
    {
        bool want = _policy.AllowTouch && _settings.CurrentValue.TouchEnabled;
        lock (_lock)
        {
            if (want == _running)
                return;
            if (want)
            {
                if (!_source.Start())
                {
                    _speechQueue.Enqueue(new Utterance(VoxStrings.Get("Touch.Unavailable"), SpeechPriority.Interrupt));
                    return;
                }
                _running = true;
                _ticker = Task.Run(TickLoopAsync);
            }
            else
            {
                _running = false;
                _source.Stop();
                _lastExplored = null;
                _lastSpoken = null;
                // Fingers down when touch went off must not complete a gesture later
                _recognizer.Process(new TouchPoint(0, 0, 0, TouchPhase.Cancel, _time.GetUtcNow()));
            }
        }
    }

    /// <summary>Runs <paramref name="command"/> if it is a touch command; returns whether it was.</summary>
    public bool TryHandle(NavigationCommand command)
    {
        switch (command)
        {
            case NavigationCommand.ToggleTouch: Toggle(); return true;
            case NavigationCommand.TouchActivate: Activate(); return true;
            case NavigationCommand.TouchNextItem: Move(next: true); return true;
            case NavigationCommand.TouchPreviousItem: Move(next: false); return true;
            case NavigationCommand.TouchNextRotorSetting: CycleRotor(1); return true;
            case NavigationCommand.TouchPreviousRotorSetting: CycleRotor(-1); return true;
            default: return false;
        }
    }

    /// <summary>Feeds one finger position (any thread).</summary>
    public void OnPoint(TouchPoint point)
    {
        lock (_lock)
        {
            if (_running)
                _recognizer.Process(point);
        }
    }

    /// <summary>Lets time pass for fingers held down (the controller's own timer calls it).</summary>
    public void Tick()
    {
        lock (_lock)
        {
            if (_running)
                _recognizer.Tick(_time.GetUtcNow());
        }
    }

    private async Task TickLoopAsync()
    {
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                Tick();
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Called by the recognizer with the lock held
    private void OnGesture(TouchGesture gesture)
    {
        if (gesture.Kind == TouchGestureKind.Explore)
        {
            Explore(gesture.X, gesture.Y);
            return;
        }
        var context = _context();
        var mode = context.Browsing ? InteractionMode.Browse : InteractionMode.Focus;
        if (_keyMap.Resolve(gesture.Kind, gesture.Fingers, context.InDocument, mode) is { } command)
        {
            _logger.LogDebug("Touch gesture {Gesture} with {Fingers} finger(s): {Command}", gesture.Kind, gesture.Fingers, command);
            _sink.Post(new NavigationCommandEvent(_time.GetUtcNow(), command));
        }
    }

    private void Explore(int x, int y)
    {
        _pending = (x, y);
        if (_lookupInFlight)
            return;
        _lookupInFlight = true;
        ExploreIdle = Task.Run(LookupLoopAsync);
    }

    private async Task LookupLoopAsync()
    {
        while (true)
        {
            (int X, int Y) point;
            lock (_lock)
            {
                if (_pending is not { } next)
                {
                    _lookupInFlight = false;
                    return;
                }
                point = next;
                _pending = null;
            }

            try
            {
                var settings = _settings.CurrentValue;
                await _uiaThread.RunAsync(() =>
                {
                    if (_targets.At(point.X, point.Y, null) is not { } target)
                        return;
                    var id = target.Object.RuntimeId is { } runtimeId ? string.Join('.', runtimeId) : string.Empty;
                    var text = ObjectNavigationCommands.Describe(_announcementBuilder, settings, target.Object);
                    var key = $"{id}\n{text}";
                    lock (_lock)
                    {
                        _lastExplored = point;
                        if (key == _lastSpoken)
                            return;
                        _lastSpoken = key;
                    }
                    if (!string.IsNullOrWhiteSpace(text))
                        _speechQueue.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
                }).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                // Something that can't be read under the finger; the next point tries again
                _logger.LogDebug(ex, "Could not read the object under the finger");
            }
        }
    }

    private void Toggle()
    {
        var settings = _settings.CurrentValue;
        bool on = !settings.TouchEnabled;
        try
        {
            _updateSettings(settings with { TouchEnabled = on });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not change the touch setting");
            return;
        }
        _speechQueue.Enqueue(new Utterance(VoxStrings.Get(on ? "Touch.On" : "Touch.Off"), SpeechPriority.Interrupt));
    }

    private void Activate()
    {
        var context = _context();
        (int X, int Y)? explored;
        lock (_lock)
            explored = _lastExplored;
        if (context.Browsing)
        {
            _sink.Post(new NavigationCommandEvent(_time.GetUtcNow(), NavigationCommand.ActivateElement));
            return;
        }
        if (explored is not { } at)
        {
            _sink.Post(new NavigationCommandEvent(_time.GetUtcNow(), NavigationCommand.ActivateNavigator));
            return;
        }
        _ = _uiaThread.RunAsync(() => _activator.ActivateAt(at.X, at.Y)).ContinueWith(t =>
        {
            if (t.IsFaulted)
                _logger.LogDebug(t.Exception, "Could not activate the object under the finger");
        }, TaskScheduler.Default);
    }

    private void Move(bool next)
    {
        var command = _context().Browsing
            ? (next ? Rotor[_rotor].Next : Rotor[_rotor].Previous)
            : (next ? NavigationCommand.NavigatorNext : NavigationCommand.NavigatorPrevious);
        _sink.Post(new NavigationCommandEvent(_time.GetUtcNow(), command));
    }

    private void CycleRotor(int step)
    {
        _rotor = (_rotor + step + Rotor.Length) % Rotor.Length;
        _speechQueue.Enqueue(new Utterance(VoxStrings.Get(Rotor[_rotor].Name), SpeechPriority.Interrupt));
    }

    private TouchKeyMap LoadKeyMap()
    {
        var map = TouchKeyMap.Load(_policy.IsSecure ? null : _userKeyMapPath, out var warnings);
        foreach (var warning in warnings)
            _logger.LogWarning("Touch keymap: {Warning}", warning);
        return map;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;
        _source.PointReceived -= OnPoint;
        _stop.Cancel();
        try { _ticker?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        _source.Stop();
        _stop.Dispose();
    }
}
