using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Ai;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Localization;
using Vox.Core.Ocr;

namespace Vox.Core.Labels;

/// <summary>Where elements of the page being browsed are shown now, without scrolling it (<see cref="Accessibility.BrowseDocumentTracker"/>).</summary>
public interface IVisibleBoundsSource
{
    /// <summary>
    /// The screen rectangle of each of <paramref name="nodes"/> (same order), or null for one that
    /// isn't on the screen.
    /// </summary>
    Task<IReadOnlyList<ScreenRect?>> VisibleBoundsAsync(IReadOnlyList<VBufferNode> nodes);
}

/// <summary>
/// Automatic labels: after a page loads, its unlabelled graphics and icon-only buttons and links
/// (no name, or a file name such as "ic_send.svg") that are on the screen are described in the
/// background through <see cref="AiAssistant"/> as a background request (the describer on this
/// computer; the cloud only when consent is "allow" and the site is not private; nobody is ever
/// asked), at most <see cref="MaxPerPage"/> per page and <see cref="MaxPerMinute"/> a minute.
/// Descriptions are kept by picture and site, so the same icon is described once, and saved as
/// custom labels marked "described", which the user's own labels replace; the page is then
/// captured again. Turned off by the <c>AutomaticLabels</c> setting; never on secure screens (no
/// labels, no capture, no AI) or with AI turned off by policy. Thread-safe.
/// </summary>
public sealed partial class AutomaticLabeler
{
    /// <summary>The most elements described for one page.</summary>
    public const int MaxPerPage = 8;

    /// <summary>The most elements described in a minute, over all pages.</summary>
    public const int MaxPerMinute = 20;

    /// <summary>Images smaller than this (either side, in pixels) are not described: spacers and dots.</summary>
    public const int MinSize = 8;

    /// <summary>Descriptions longer than this are cut at a word: a label is a name, not a paragraph.</summary>
    public const int MaxLabelLength = 80;

    private const int CacheCapacity = 512;

    private readonly AiAssistant _ai;
    private readonly IScreenCapture _capture;
    private readonly IVisibleBoundsSource _bounds;
    private readonly IAiContextSource _context;
    private readonly CustomLabelStore _store;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly Action _recapture;
    private readonly ILogger<AutomaticLabeler> _logger;
    private readonly TimeProvider _time;
    private readonly TimeSpan _settleTime;
    private readonly object _lock = new();
    private readonly Queue<DateTimeOffset> _recent = new();
    private readonly Dictionary<string, int> _perPage = new(StringComparer.Ordinal);
    private readonly HashSet<string> _tried = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _byPicture = new(StringComparer.Ordinal);
    private readonly Queue<string> _pictureOrder = new();
    private CancellationTokenSource? _running;

    public AutomaticLabeler(AiAssistant ai, IScreenCapture capture, IVisibleBoundsSource bounds, IAiContextSource context,
        CustomLabelStore store, IOptionsMonitor<VoxSettings> settings, Action recapture, ILogger<AutomaticLabeler> logger,
        TimeProvider? time = null, TimeSpan? settleTime = null)
    {
        _ai = ai;
        _capture = capture;
        _bounds = bounds;
        _context = context;
        _store = store;
        _settings = settings;
        _recapture = recapture;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        // Let the browser draw the page before pictures of it are taken
        _settleTime = settleTime ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>Raised when a run has finished (tests), with the number of labels it added.</summary>
    public event EventHandler<int>? RunFinished;

    /// <summary>Whether anything would be described now (setting on, labels kept, capture and AI allowed and set up).</summary>
    public bool IsActive =>
        _settings.CurrentValue.AutomaticLabels
        && _store.IsAvailable
        && _capture.IsAvailable
        && _ai.IsSetUp
        && _ai.Consent.ForLocal().Decision != AiConsentDecision.Refused;

    /// <summary>
    /// A whole page was loaded (pipeline thread): picks its unlabelled elements now and describes
    /// them in the background, cancelling a run for an earlier page.
    /// </summary>
    public Task OnDocumentLoaded(VBufferDocument document)
    {
        if (!IsActive || CustomLabelSites.SiteOf(document.Root.Value) is not { } site)
            return Task.CompletedTask;
        var candidates = Candidates(document);
        if (candidates.Count == 0)
            return Task.CompletedTask;

        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref _running, cancellation)?.Cancel();
        return RunAsync(document, site, candidates, cancellation);
    }

    /// <summary>Stops a run in progress (shutting down).</summary>
    public void Cancel() => Interlocked.Exchange(ref _running, null)?.Cancel();

    /// <summary>
    /// The elements of <paramref name="document"/> worth describing, in page order: graphics,
    /// and buttons and links with nothing but a picture in them, that have no custom label and
    /// no useful name or text.
    /// </summary>
    public static IReadOnlyList<VBufferNode> Candidates(VBufferDocument document)
    {
        var found = new List<VBufferNode>();
        for (var node = document.Root.NextInOrder; node is not null; node = node.NextInOrder)
        {
            if (!string.IsNullOrEmpty(node.CustomLabel) || HasLabelledAncestor(node))
                continue;
            if (IsIconControl(node))
            {
                if (IsUnhelpful(node.Name) && IsUnhelpful(TextOf(node, document.FlatText)))
                    found.Add(node);
            }
            else if (PageElements.IsGraphic(node) && ControlAround(node) is null && IsUnhelpful(node.Name))
            {
                found.Add(node);
            }
        }
        return found;
    }

    /// <summary>
    /// Whether a name says nothing: empty, a file name or address ("ic_send.svg", "IMG_2041",
    /// "https://cdn.example.com/a.png"), or a word such as "image" or "icon".
    /// </summary>
    public static bool IsUnhelpful(string? name)
    {
        var text = name?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return true;
        return FileName().IsMatch(text) || Address().IsMatch(text) || PlaceholderWord().IsMatch(text);
    }

    [GeneratedRegex(@"^[\w\-.~%]+\.(png|jpe?g|gif|svg|webp|bmp|ico|avif|tiff?)$", RegexOptions.IgnoreCase)]
    private static partial Regex FileName();

    [GeneratedRegex(@"^(https?:|data:|blob:|/)\S*$", RegexOptions.IgnoreCase)]
    private static partial Regex Address();

    [GeneratedRegex(@"^(img|image|icon|ic|graphic|picture|pic|photo|dsc|logo|svg|button|btn)?[\s_\-]*\d*$|^(img|ic|icon|btn|dsc)[_\-][\w\-]*$", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderWord();

    private async Task RunAsync(VBufferDocument document, string site, IReadOnlyList<VBufferNode> candidates, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var pageKey = PageKey(document);
        var added = new List<CustomLabel>();
        try
        {
            await Task.Delay(_settleTime, _time, token).ConfigureAwait(false);
            var target = (await _context.WindowTargetAsync().ConfigureAwait(false)) with { IsPassword = false, Site = document.Root.Value };

            // Only what was never tried on this page, within its limit
            var pending = new List<(VBufferNode Node, string Signature)>();
            lock (_lock)
            {
                _perPage.TryGetValue(pageKey, out var done);
                foreach (var node in candidates)
                {
                    if (done + pending.Count >= MaxPerPage)
                        break;
                    var signature = ElementSignature.Of(node, document.FlatText);
                    if (_store.Find(site, signature) is null && !_tried.Contains(TriedKey(site, signature)))
                        pending.Add((node, signature));
                }
            }
            if (pending.Count == 0)
                return;

            var bounds = await _bounds.VisibleBoundsAsync(pending.Select(p => p.Node).ToList()).ConfigureAwait(false);
            for (int i = 0; i < pending.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (bounds.ElementAtOrDefault(i) is not { } area || area.Width < MinSize || area.Height < MinSize)
                    continue;
                if (!TakeTurn(pageKey))
                    break;
                var (node, signature) = pending[i];
                lock (_lock) _tried.Add(TriedKey(site, signature));

                var image = await Task.Run(() => _capture.Capture(area), token).ConfigureAwait(false);
                if (image is null)
                    continue;
                var label = await DescribeAsync(site, image, target, token).ConfigureAwait(false);
                if (label is not null)
                    added.Add(new CustomLabel(site, signature, label, Described: true));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Automatic labels failed");
        }
        finally
        {
            Interlocked.CompareExchange(ref _running, null, cancellation);
            cancellation.Dispose();
        }

        // The user's own labels win: one given meanwhile is never replaced
        var kept = added.Where(l => _store.Find(l.Site, l.Signature) is null).ToList();
        if (kept.Count > 0 && _store.SetAll(kept))
        {
            // Never logged: the descriptions are about what was on the screen
            _logger.LogDebug("Automatic labels: {Count} elements described", kept.Count);
            _recapture();
        }
        RunFinished?.Invoke(this, kept.Count);
    }

    /// <summary>The picture's description, from what this site's same picture was called before, else the describer.</summary>
    private async Task<string?> DescribeAsync(string site, CapturedImage image, AiTarget target, CancellationToken token)
    {
        var pictureKey = site + "|" + image.Hash();
        lock (_lock)
        {
            if (_byPicture.TryGetValue(pictureKey, out var known))
                return known;
        }
        var answer = await _ai.AskAsync(new AiRequest(AiTask.DescribeImage, image, AiPrompts.LabelQuestion,
            Language: VoxStrings.EffectiveCulture.Name), target, token, background: true).ConfigureAwait(false);
        if (!answer.HasText)
            return null;
        var label = Shorten(answer.Text!);
        lock (_lock)
        {
            _byPicture[pictureKey] = label;
            _pictureOrder.Enqueue(pictureKey);
            while (_pictureOrder.Count > CacheCapacity)
                _byPicture.Remove(_pictureOrder.Dequeue());
        }
        return label;
    }

    /// <summary>Counts one description against the page's and the minute's limits; false when either is reached.</summary>
    private bool TakeTurn(string pageKey)
    {
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            while (_recent.Count > 0 && now - _recent.Peek() >= TimeSpan.FromMinutes(1))
                _recent.Dequeue();
            _perPage.TryGetValue(pageKey, out var done);
            if (_recent.Count >= MaxPerMinute || done >= MaxPerPage)
                return false;
            _recent.Enqueue(now);
            _perPage[pageKey] = done + 1;
            return true;
        }
    }

    /// <summary>The first sentence of a description, without a closing full stop, cut at a word to <see cref="MaxLabelLength"/>.</summary>
    public static string Shorten(string description)
    {
        var text = description.ReplaceLineEndings(" ").Trim();
        int stop = text.IndexOfAny(['.', '!', '?']);
        while (stop > 0 && stop + 1 < text.Length && !char.IsWhiteSpace(text[stop + 1]))
            stop = text.IndexOfAny(['.', '!', '?'], stop + 1);
        if (stop > 0)
            text = text[..stop];
        text = text.Trim().TrimEnd('.', ',', ';', ':');
        if (text.Length > MaxLabelLength)
        {
            int space = text.LastIndexOf(' ', MaxLabelLength);
            text = text[..(space > 0 ? space : MaxLabelLength)].TrimEnd(',', ';', ':');
        }
        return text;
    }

    // A page is its address (a reload of it shares its limit)
    private static string PageKey(VBufferDocument document) => document.Root.Value;

    private static string TriedKey(string site, string signature) => site + "|" + signature;

    private static bool IsIconControl(VBufferNode node)
    {
        if (node.IsLink)
            return true;
        if (!node.IsFocusable)
            return false;
        var role = node.AriaRole?.Trim().ToLowerInvariant();
        return node.ControlType is "Button" or "SplitButton" or "MenuItem" or "TabItem"
            || role is "button" or "menuitem" or "tab" or "link";
    }

    private static VBufferNode? ControlAround(VBufferNode node)
    {
        for (var parent = node.Parent; parent?.Parent is not null; parent = parent.Parent)
        {
            if (parent.IsLink || parent.IsFocusable)
                return parent;
        }
        return null;
    }

    private static bool HasLabelledAncestor(VBufferNode node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (!string.IsNullOrEmpty(parent.CustomLabel))
                return true;
        }
        return false;
    }

    /// <summary>The text of <paramref name="node"/> and everything inside it.</summary>
    private static string TextOf(VBufferNode node, string flatText)
    {
        int start = node.TextRange.Start, end = node.TextRange.End;
        for (var next = node.NextInOrder; next is not null && IsInside(next, node); next = next.NextInOrder)
        {
            if (next.HasText)
            {
                start = Math.Min(start, next.TextRange.Start);
                end = Math.Max(end, next.TextRange.End);
            }
        }
        start = Math.Clamp(start, 0, flatText.Length);
        end = Math.Clamp(end, start, flatText.Length);
        return flatText[start..end].Trim();
    }

    private static bool IsInside(VBufferNode node, VBufferNode ancestor)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (ReferenceEquals(parent, ancestor))
                return true;
        }
        return false;
    }
}
