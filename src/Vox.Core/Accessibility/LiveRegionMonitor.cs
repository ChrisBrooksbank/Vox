using Vox.Core.Pipeline;

namespace Vox.Core.Accessibility;

/// <summary>
/// Maintains a dictionary of last-known text per live region source and implements:
/// - Diff detection: only announces when text has changed
/// - Polite throttling: at most 1 announcement per 500ms per source
/// - Assertive bypass: assertive regions are always announced immediately
/// </summary>
public sealed class LiveRegionMonitor
{
    private const int PoliteCooldownMs = 500;

    // sourceId -> last known text
    private readonly Dictionary<string, string> _lastKnownText = new();

    // sourceId -> timestamp of last polite announcement
    private readonly Dictionary<string, DateTimeOffset> _lastPoliteAnnouncement = new();

    private readonly object _lock = new();

    private readonly Func<DateTimeOffset> _clock;

    public LiveRegionMonitor() : this(() => DateTimeOffset.UtcNow) { }

    /// <summary>
    /// Constructor allowing clock injection for testing.
    /// </summary>
    public LiveRegionMonitor(Func<DateTimeOffset> clock)
    {
        _clock = clock;
    }

    /// <summary>
    /// Processes a live region update. Returns true if the event should be announced.
    /// </summary>
    /// <param name="sourceId">Unique identifier for the live region element (e.g. "1,2,3" from RuntimeId).</param>
    /// <param name="text">Current text content of the live region.</param>
    /// <param name="politeness">Politeness level of the live region.</param>
    public bool ShouldAnnounce(string? sourceId, string text, LiveRegionPoliteness politeness)
    {
        // If no sourceId, we can't track state — always announce
        if (string.IsNullOrEmpty(sourceId))
            return !string.IsNullOrWhiteSpace(text);

        lock (_lock)
        {
            // Diff: skip if text hasn't changed
            if (_lastKnownText.TryGetValue(sourceId, out var last) && last == text)
                return false;

            // Text has changed — update last known
            _lastKnownText[sourceId] = text;

            // Empty text is not interesting
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Assertive: immediate, no throttle
            if (politeness == LiveRegionPoliteness.Assertive)
                return true;

            // Polite: throttle to 1 per 500ms
            var now = _clock();
            if (_lastPoliteAnnouncement.TryGetValue(sourceId, out var lastAnnounced))
            {
                var elapsed = now - lastAnnounced;
                if (elapsed.TotalMilliseconds < PoliteCooldownMs)
                    return false;
            }

            _lastPoliteAnnouncement[sourceId] = now;
            return true;
        }
    }

    /// <summary>
    /// Clears all tracked state (useful for testing or reset scenarios).
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _lastKnownText.Clear();
            _lastPoliteAnnouncement.Clear();
        }
    }
}
