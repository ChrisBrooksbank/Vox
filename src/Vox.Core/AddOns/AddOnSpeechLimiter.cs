using Vox.Sdk;

namespace Vox.Core.AddOns;

/// <summary>
/// Keeps one add-on from drowning out Vox: it may say <see cref="MaxUrgent"/> utterances at
/// <see cref="SpeechPriority.Interrupt"/> or <see cref="SpeechPriority.High"/> within
/// <see cref="Window"/>; past that they are said at Normal priority instead (still heard, but they
/// no longer cut off or jump ahead of Vox's own speech). Thread-safe.
/// </summary>
public sealed class AddOnSpeechLimiter(TimeProvider? time = null)
{
    public const int MaxUrgent = 5;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Queue<DateTimeOffset> _urgent = new();
    private readonly object _lock = new();

    /// <summary>True once a limit was applied, for logging only the first time.</summary>
    public bool HasLimited { get; private set; }

    /// <summary>The priority to speak at: the requested one, or Normal when the add-on is over its limit.</summary>
    public SpeechPriority Limit(SpeechPriority requested)
    {
        if (requested is not (SpeechPriority.Interrupt or SpeechPriority.High))
            return requested;
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            while (_urgent.Count > 0 && now - _urgent.Peek() >= Window)
                _urgent.Dequeue();
            if (_urgent.Count >= MaxUrgent)
            {
                HasLimited = true;
                return SpeechPriority.Normal;
            }
            _urgent.Enqueue(now);
            return requested;
        }
    }
}
