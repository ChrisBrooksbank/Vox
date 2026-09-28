using Vox.Core.Pipeline;

namespace Vox.Core.Accessibility;

/// <summary>
/// Decides what to say for live region updates:
/// - Diff detection: unchanged text is not announced; when text was appended to the previous
///   text (a chat log, a status log) only the added part is announced (aria-relevant="additions")
/// - Polite throttling: at most 1 announcement per 500ms per source. Updates inside the cooldown
///   are not dropped — the latest is kept and announced when the cooldown ends (<see cref="FlushPending"/>)
/// - Assertive bypass: assertive regions are always announced immediately
/// - Bounded memory: at most <see cref="MaxSources"/> regions are tracked (least recently seen evicted)
/// </summary>
public sealed class LiveRegionMonitor
{
    private const int PoliteCooldownMs = 500;
    public const int MaxSources = 256;

    private sealed class RegionState
    {
        public string LastText = string.Empty;
        public DateTimeOffset? LastPoliteAnnouncement;
        public string? Pending;
        public long LastSeen;
    }

    private readonly Dictionary<string, RegionState> _regions = new();
    private readonly object _lock = new();
    private readonly Func<DateTimeOffset> _clock;
    private long _sequence;

    public LiveRegionMonitor() : this(() => DateTimeOffset.UtcNow) { }

    /// <summary>
    /// Constructor allowing clock injection for testing.
    /// </summary>
    public LiveRegionMonitor(Func<DateTimeOffset> clock)
    {
        _clock = clock;
    }

    /// <summary>Number of regions currently tracked.</summary>
    public int TrackedSourceCount
    {
        get { lock (_lock) return _regions.Count; }
    }

    /// <summary>
    /// Processes a live region update. Returns true if something should be announced now.
    /// </summary>
    public bool ShouldAnnounce(string? sourceId, string text, LiveRegionPoliteness politeness) =>
        Evaluate(sourceId, text, politeness, out _) is not null;

    /// <summary>
    /// Processes a live region update and returns the text to announce now, or null.
    /// When a polite update is held back by the cooldown, <paramref name="retryAfter"/> says when
    /// to call <see cref="FlushPending"/> for this source.
    /// </summary>
    /// <param name="sourceId">Unique identifier for the live region element (e.g. "1,2,3" from RuntimeId).</param>
    /// <param name="text">Current text content of the live region.</param>
    /// <param name="politeness">Politeness level of the live region.</param>
    public string? Evaluate(string? sourceId, string text, LiveRegionPoliteness politeness, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;

        // If no sourceId, we can't track state — always announce
        if (string.IsNullOrEmpty(sourceId))
            return string.IsNullOrWhiteSpace(text) ? null : text;

        lock (_lock)
        {
            var region = GetRegion(sourceId);

            // Diff: skip if text hasn't changed
            if (region.LastText == text)
                return null;

            bool isAddition = IsAddition(region.LastText, text);
            var announcement = isAddition ? text[region.LastText.Length..].Trim() : text;
            region.LastText = text;

            // Empty text is not interesting
            if (string.IsNullOrWhiteSpace(announcement))
                return null;

            // Assertive: immediate, no throttle
            if (politeness == LiveRegionPoliteness.Assertive)
                return announcement;

            // Polite: throttle to 1 per 500ms, keeping what arrives during the cooldown
            var now = _clock();
            if (region.LastPoliteAnnouncement is { } last)
            {
                var remaining = TimeSpan.FromMilliseconds(PoliteCooldownMs) - (now - last);
                if (remaining > TimeSpan.Zero)
                {
                    region.Pending = isAddition && region.Pending is not null
                        ? region.Pending + " " + announcement
                        : announcement;
                    retryAfter = remaining;
                    return null;
                }
            }

            region.LastPoliteAnnouncement = now;
            region.Pending = null;
            return announcement;
        }
    }

    /// <summary>
    /// Returns the text held back for <paramref name="sourceId"/> once its cooldown has passed, or null
    /// (nothing pending, or still cooling down — in which case <paramref name="retryAfter"/> is set).
    /// </summary>
    public string? FlushPending(string sourceId, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        lock (_lock)
        {
            if (!_regions.TryGetValue(sourceId, out var region) || region.Pending is null)
                return null;

            var now = _clock();
            if (region.LastPoliteAnnouncement is { } last)
            {
                var remaining = TimeSpan.FromMilliseconds(PoliteCooldownMs) - (now - last);
                if (remaining > TimeSpan.Zero)
                {
                    retryAfter = remaining;
                    return null;
                }
            }

            var pending = region.Pending;
            region.Pending = null;
            region.LastPoliteAnnouncement = now;
            return pending;
        }
    }

    /// <summary>
    /// Clears all tracked state (useful for testing or reset scenarios).
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _regions.Clear();
        }
    }

    /// <summary>
    /// True when <paramref name="text"/> is <paramref name="previous"/> with more text appended at a
    /// word boundary (so "1" → "12" is a change, not an addition of "2").
    /// </summary>
    private static bool IsAddition(string previous, string text) =>
        previous.Length > 0
        && text.Length > previous.Length
        && text.StartsWith(previous, StringComparison.Ordinal)
        && (char.IsWhiteSpace(text[previous.Length]) || char.IsWhiteSpace(previous[^1]));

    // Caller holds _lock
    private RegionState GetRegion(string sourceId)
    {
        if (!_regions.TryGetValue(sourceId, out var region))
        {
            if (_regions.Count >= MaxSources)
                EvictLeastRecentlySeen();
            region = new RegionState();
            _regions[sourceId] = region;
        }
        region.LastSeen = ++_sequence;
        return region;
    }

    private void EvictLeastRecentlySeen()
    {
        string? oldestKey = null;
        long oldest = long.MaxValue;
        foreach (var (key, state) in _regions)
        {
            if (state.LastSeen < oldest)
            {
                oldest = state.LastSeen;
                oldestKey = key;
            }
        }
        if (oldestKey is not null)
            _regions.Remove(oldestKey);
    }
}
