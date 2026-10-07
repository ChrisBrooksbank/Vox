using System.Runtime.InteropServices;
using Interop.UIAutomationClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using TextUnit = Vox.Core.Text.TextUnit;

namespace Vox.Core.Accessibility;

/// <summary>The mouse pointer's screen position (any thread).</summary>
public interface IMousePointer
{
    /// <summary>The pointer's position in screen coordinates, or null when it can't be read.</summary>
    (int X, int Y)? GetPosition();
}

/// <summary><see cref="IMousePointer"/> through GetCursorPos.</summary>
public sealed class Win32MousePointer : IMousePointer
{
    public (int X, int Y)? GetPosition() => GetCursorPos(out var point) ? (point.X, point.Y) : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);
}

/// <summary>What is under the pointer: the object, and the text unit there when it has text.</summary>
public readonly record struct PointerTarget(FocusChangedEvent Object, string? Text);

/// <summary>Finds what is at a screen point. UIA implementation: UIA thread only.</summary>
public interface IPointerTargetSource
{
    /// <summary>
    /// The object at (<paramref name="x"/>, <paramref name="y"/>) and, when <paramref name="unit"/>
    /// is given and the object has text, that unit of text there; null when there is nothing.
    /// </summary>
    PointerTarget? At(int x, int y, TextUnit? unit);
}

/// <summary><see cref="IPointerTargetSource"/> through ElementFromPoint and TextPattern.RangeFromPoint.</summary>
public sealed class UIAPointerTargetSource(UIAProvider provider) : IPointerTargetSource
{
    public PointerTarget? At(int x, int y, TextUnit? unit)
    {
        var point = new tagPOINT { x = x, y = y };
        var element = provider.Automation.ElementFromPointBuildCache(point, provider.CacheRequest);
        if (element is null)
            return null;
        var description = UIAEventSubscriber.DescribeCached(element);
        string? text = null;
        // A password box's text would give it away
        if (unit is { } textUnit && !description.IsPassword && UIATextDocument.TryCreate(element) is { } document)
            text = document.TextAt(point, textUnit);
        return new PointerTarget(description, text);
    }
}

/// <summary>
/// Mouse tracking: speaks what is under the pointer as it moves (setting, off by default) — the
/// object, or the line or word of text there. The pointer is polled, with at most one lookup per
/// <see cref="LookupInterval"/> and never two at once, so a moving mouse can't flood the UIA thread.
/// Something is said only when what is under the pointer changes.
/// </summary>
public sealed class MouseTracker : IDisposable
{
    /// <summary>The shortest time between two lookups.</summary>
    public static readonly TimeSpan LookupInterval = TimeSpan.FromMilliseconds(100);

    private const int MaxTextLength = 1000;

    private readonly UIAThread _uiaThread;
    private readonly IMousePointer _pointer;
    private readonly IPointerTargetSource _source;
    private readonly SpeechQueue _speechQueue;
    private readonly AnnouncementBuilder _announcementBuilder;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly Action<VoxSettings> _updateSettings;
    private readonly ILogger<MouseTracker> _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    // Touched only by the polling loop (one tick at a time)
    private (int X, int Y)? _lastPosition;
    private DateTimeOffset _lastLookup = DateTimeOffset.MinValue;
    // Touched only on the UIA thread
    private string? _lastSpoken;

    public MouseTracker(UIAThread uiaThread, IMousePointer pointer, IPointerTargetSource source, SpeechQueue speechQueue,
        AnnouncementBuilder announcementBuilder, IOptionsMonitor<VoxSettings> settings, Action<VoxSettings> updateSettings,
        ILogger<MouseTracker> logger, Func<DateTimeOffset>? clock = null)
    {
        _uiaThread = uiaThread;
        _pointer = pointer;
        _source = source;
        _speechQueue = speechQueue;
        _announcementBuilder = announcementBuilder;
        _settings = settings;
        _updateSettings = updateSettings;
        _logger = logger;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Runs <paramref name="command"/> if it is a mouse tracking command; returns whether it was.</summary>
    public bool TryHandle(NavigationCommand command)
    {
        if (command != NavigationCommand.ToggleMouseTracking)
            return false;
        var settings = _settings.CurrentValue;
        bool on = !settings.MouseTracking;
        try
        {
            _updateSettings(settings with { MouseTracking = on });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not change the mouse tracking setting");
        }
        _speechQueue.Enqueue(new Utterance(on ? "Mouse tracking on" : "Mouse tracking off", SpeechPriority.Interrupt));
        return true;
    }

    /// <summary>Starts polling the pointer.</summary>
    public void Start()
    {
        _loop ??= Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(LookupInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                    await TickAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    /// <summary>
    /// One poll: if tracking is on, the pointer has moved and the last lookup was long enough
    /// ago, looks up what is under it and says it if that has changed. Not reentrant.
    /// </summary>
    public async Task TickAsync()
    {
        var settings = _settings.CurrentValue;
        if (!settings.MouseTracking)
        {
            _lastPosition = null;
            return;
        }

        (int X, int Y)? position;
        try { position = _pointer.GetPosition(); }
        catch { return; }
        if (position is not { } point)
            return;
        // Just turned on: nothing is said until the pointer moves
        if (_lastPosition is null)
        {
            _lastPosition = point;
            return;
        }
        if (_lastPosition == point)
            return;
        var now = _clock();
        if (now - _lastLookup < LookupInterval)
            return;

        _lastPosition = point;
        _lastLookup = now;
        TextUnit? unit = settings.MouseTextUnit switch
        {
            MouseTextUnit.Line => TextUnit.Line,
            MouseTextUnit.Word => TextUnit.Word,
            _ => null,
        };
        try
        {
            await _uiaThread.RunAsync(() =>
            {
                if (_source.At(point.X, point.Y, unit) is not { } target)
                    return;
                var (key, text) = Speech(target, settings);
                if (key == _lastSpoken)
                    return;
                _lastSpoken = key;
                if (!string.IsNullOrWhiteSpace(text))
                    _speechQueue.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
            }).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            // The pointer is over something that can't be read; try again when it moves
            _logger.LogDebug(ex, "Could not read the object under the mouse pointer");
        }
    }

    /// <summary>What to say for a target, and what identifies it (to say it only when it changes).</summary>
    private (string Key, string Text) Speech(PointerTarget target, VoxSettings settings)
    {
        var id = target.Object.RuntimeId is { } runtimeId ? string.Join('.', runtimeId) : string.Empty;
        var text = target.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            if (text.Length > MaxTextLength)
                text = text[..MaxTextLength];
            return ($"{id}\n{text}", text);
        }
        var description = ObjectNavigationCommands.Describe(_announcementBuilder, settings, target.Object);
        return ($"{id}\n{description}", description);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException) { }
        _stop.Dispose();
    }
}
