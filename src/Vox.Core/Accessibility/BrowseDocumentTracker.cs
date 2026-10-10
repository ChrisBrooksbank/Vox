using Interop.UIAutomationClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;

namespace Vox.Core.Accessibility;

/// <summary>
/// Connects the virtual buffer to live web content.
///
/// On every focus change it looks for a web Document ancestor (Chromium or Gecko) of the focused
/// element. When focus enters a new document it captures the document's subtree in one cached
/// UIA call, builds a <see cref="VBufferDocument"/> and posts a <see cref="DocumentChangedEvent"/>;
/// when focus leaves web content it posts a null document. StructureChanged events inside the
/// document are debounced and re-captured as <see cref="SubtreeChangedEvent"/>s for
/// <see cref="IncrementalUpdater"/>.
///
/// All UIA work runs on <see cref="UIAThread"/>; the COM document element never leaves it.
/// </summary>
public sealed class BrowseDocumentTracker : IBrowseDocumentActions, IDisposable
{
    private const int UIA_DocumentControlTypeId = 50030;
    private const int MaxAncestorDepth = 64;
    private const int StructureDebounceMs = 300;
    private const int StructureMaxWaitMs = 1000;
    private const int FullRecaptureThreshold = 20;

    private readonly UIAThread _uiaThread;
    private readonly UIAProvider _uiaProvider;
    private readonly UIAEventSubscriber _eventSubscriber;
    private readonly IEventSink _eventSink;
    private readonly ILogger<BrowseDocumentTracker> _logger;
    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly IOptionsMonitor<VoxSettings>? _settings;
    private readonly IMouseInput? _mouse;

    // STA-thread state
    private IUIAutomationElement? _documentRoot;
    private int[]? _documentRuntimeId;

    // Pending structure changes, keyed by runtime id string (guarded by _pendingLock)
    private readonly object _pendingLock = new();
    private readonly Dictionary<string, int[]> _pendingChanges = new();
    private bool _fullRecapturePending;
    private long _firstPendingTick;
    private readonly System.Threading.Timer _debounceTimer;
    private bool _disposed;

    public BrowseDocumentTracker(
        UIAThread uiaThread,
        UIAProvider uiaProvider,
        UIAEventSubscriber eventSubscriber,
        IEventSink eventSink,
        ILogger<BrowseDocumentTracker> logger,
        IOptionsMonitor<VoxSettings>? settings = null,
        IMouseInput? mouse = null)
    {
        _settings = settings;
        _mouse = mouse;
        _uiaThread = uiaThread;
        _uiaProvider = uiaProvider;
        _eventSubscriber = eventSubscriber;
        _eventSink = eventSink;
        _logger = logger;
        _debounceTimer = new System.Threading.Timer(_ => ProcessPendingChanges(), null, Timeout.Infinite, Timeout.Infinite);
    }

    // -------------------------------------------------------------------------
    // Focus tracking
    // -------------------------------------------------------------------------

    /// <summary>
    /// Re-evaluates which web document (if any) contains the focused element.
    /// Safe to call from any thread; the work runs on the UIA thread.
    /// </summary>
    /// <param name="focusSequence">
    /// The focus change this check is for (see <see cref="BrowseModeController.FocusSequence"/>),
    /// echoed in <see cref="FocusInDocumentEvent"/> so a report about an earlier focus is ignored.
    /// </param>
    public Task OnFocusChangedAsync(long focusSequence = 0)
    {
        // Coalesce: while a check is waiting to run (e.g. the UIA thread is busy with a slow page),
        // later focus changes only update which focus it reports on — no backlog builds up
        Interlocked.Exchange(ref _pendingFocusSequence, focusSequence);
        if (Interlocked.Exchange(ref _detectQueued, 1) == 1)
            return Task.CompletedTask;

        return RunOnUiaThread(() =>
        {
            // Cleared first, so a focus change during this check queues another
            Interlocked.Exchange(ref _detectQueued, 0);
            DetectDocument(Interlocked.Read(ref _pendingFocusSequence));
        }, "detecting web document");
    }

    private int _detectQueued;
    private long _pendingFocusSequence;

    /// <summary>
    /// After the UIA thread was replaced: forgets the loaded document (its element and event
    /// subscriptions belong to the old automation object) and loads the focused one again. The
    /// reading position survives, as for any return to a recently visited document.
    /// </summary>
    public Task ReloadAfterThreadReplacedAsync()
    {
        Interlocked.Exchange(ref _detectQueued, 0);
        return RunOnUiaThread(() =>
        {
            ClearPendingChanges();
            _capturedIds.Clear();
            _documentRoot = null;
            _documentRuntimeId = null;
            DetectDocument(Interlocked.Read(ref _pendingFocusSequence));
        }, "reloading the document after the UIA thread was replaced");
    }

    private void DetectDocument() => DetectDocument(0);

    private void DetectDocument(long focusSequence)
    {
        var automation = _uiaProvider.Automation;
        var cacheRequest = _uiaProvider.CacheRequest;

        var focused = automation.GetFocusedElementBuildCache(cacheRequest);
        if (focused is null) return;

        // Vox's own windows (e.g. the Elements List) must not unload the document
        if (TryGet(() => focused.CachedProcessId) == _ownProcessId)
            return;

        // Focus moving within the loaded document (the common case, every Tab): no need to walk
        // the ancestor chain, one cross-process call per level, to find the document again
        var focusedId = UIAEventSubscriber.TryGetRuntimeId(focused);
        if (_documentRoot is not null && _documentRuntimeId is not null && _capturedIds.Contains(RuntimeIdKey(focusedId)))
        {
            _eventSink.Post(new FocusInDocumentEvent(DateTimeOffset.UtcNow, _documentRuntimeId, focusedId, focusSequence));
            return;
        }

        var document = FindWebDocument(automation, focused, cacheRequest);
        if (document is null)
        {
            if (_documentRoot is not null)
                UnloadDocument();
            return;
        }

        var documentId = UIAEventSubscriber.TryGetRuntimeId(document);
        if (_documentRuntimeId is not null && documentId.AsSpan().SequenceEqual(_documentRuntimeId))
        {
            // Same document (the cursor follows focus via FocusChangedEvent.RuntimeId), possibly
            // an element the buffer doesn't contain yet: browse keys apply again
            _eventSink.Post(new FocusInDocumentEvent(DateTimeOffset.UtcNow, _documentRuntimeId, focusedId, focusSequence));
            return;
        }

        LoadDocument(document, documentId, focusedId, focused);
    }

    /// <summary>
    /// Walks up from the focused element to the outermost web Document (so iframes resolve to the page).
    /// </summary>
    private static IUIAutomationElement? FindWebDocument(
        IUIAutomation automation, IUIAutomationElement focused, IUIAutomationCacheRequest cacheRequest)
    {
        var walker = automation.ControlViewWalker;

        // The ancestor chain first: whether it is inside a Firefox window decides what is web content
        var chain = new List<IUIAutomationElement>();
        for (var element = focused; element is not null && chain.Count < MaxAncestorDepth;
             element = TryGet(() => walker.GetParentElementBuildCache(element, cacheRequest)))
            chain.Add(element);
        bool inFirefox = chain.Any(e => WebContent.IsFirefoxWindowClass(TryGet(() => e.CachedClassName)));

        IUIAutomationElement? found = null;
        foreach (var element in chain)
        {
            var isWeb = WebContent.IsWebElement(TryGet(() => element.CachedFrameworkId), inFirefox);
            if (isWeb && TryGet(() => element.CachedControlType) == UIA_DocumentControlTypeId)
                found = element;
            else if (found is not null && !isWeb)
                break; // Left the browser's web content
        }

        return found;
    }

    private void LoadDocument(IUIAutomationElement document, int[] documentId, int[] focusedId, IUIAutomationElement? focused = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Subscribe to the document's changes before capturing it, so nothing that changes in
        // between is missed (a duplicate update is harmless; a missed one is not). Changes that
        // arrive during the capture wait in the pending set until the document is recorded.
        ClearPendingChanges();
        _eventSubscriber.SetDocumentScope(document);

        // A large page takes a while to capture: the part around the focus (or the top of the
        // page) is captured and posted first, so browsing can start before the rest arrives
        PostPartialDocument(document, documentId, focusedId, focused);

        VBufferDocument buffer;
        try
        {
            var cached = _uiaProvider.WithDocumentCaptureTimeout(() => document.BuildUpdatedCache(_uiaProvider.SubtreeCacheRequest));
            var snapshot = UIAElementSnapshot.Capture(cached);
            buffer = new VBufferBuilder { ScreenLayout = _settings?.CurrentValue.ScreenLayout ?? true }.Build(snapshot);
            RememberWholeCapture(snapshot);
        }
        catch (Exception ex)
        {
            // Busy page or UIA timeout. Don't record the document as loaded — that would leave the
            // previous page's buffer in use and make every later focus change look like "same
            // document". Drop the old buffer and try once more shortly.
            _logger.LogWarning(ex, "Could not capture web document");
            _eventSubscriber.SetDocumentScope(null);
            _capturedIds.Clear();
            _documentRoot = null;
            _documentRuntimeId = null;
            _eventSink.Post(new DocumentChangedEvent(DateTimeOffset.UtcNow, null));
            ScheduleRetry(documentId);
            return;
        }

        _documentRoot = document;
        _documentRuntimeId = documentId;
        _retriedDocumentKey = null;
        _fullRecaptureFailures = 0;

        _logger.LogInformation("Virtual buffer built: {Nodes} nodes in {Ms}ms", buffer.AllNodes.Count, sw.ElapsedMilliseconds);
        _eventSink.Post(new DocumentChangedEvent(DateTimeOffset.UtcNow, buffer, focusedId, ProcessNameOf(document)));
    }

    // Document whose failed capture has already been retried (UIA thread only)
    private string? _retriedDocumentKey;
    private const int CaptureRetryDelayMs = 500;

    private void ScheduleRetry(int[] documentId)
    {
        var key = string.Join(",", documentId);
        if (key == _retriedDocumentKey)
            return; // retry at most once per document; later focus changes will try again
        _retriedDocumentKey = key;

        _ = Task.Delay(CaptureRetryDelayMs).ContinueWith(
            _ => RunOnUiaThread(DetectDocument, "retrying document capture"),
            TaskScheduler.Default);
    }

    private void UnloadDocument()
    {
        _capturedIds.Clear();
        _documentRoot = null;
        _documentRuntimeId = null;
        ClearPendingChanges();
        _eventSubscriber.SetDocumentScope(null);
        _eventSink.Post(new DocumentChangedEvent(DateTimeOffset.UtcNow, null));
    }

    // -------------------------------------------------------------------------
    // Structure changes
    // -------------------------------------------------------------------------

    /// <summary>
    /// Queues a structure change for re-capture. Changes are debounced so bursts of events
    /// (common while pages load) produce one update per element, but never wait more than
    /// <see cref="StructureMaxWaitMs"/> so pages that change constantly still get updated.
    /// </summary>
    public void OnStructureChanged(int[] runtimeId)
    {
        if (runtimeId.Length == 0) return;

        lock (_pendingLock)
        {
            if (_disposed) return;
            _pendingChanges[string.Join(",", runtimeId)] = runtimeId;
            ScheduleFlush();
        }
    }

    public void RequestRecapture(int[]? runtimeId)
    {
        if (runtimeId is not null)
        {
            OnStructureChanged(runtimeId);
            return;
        }

        lock (_pendingLock)
        {
            if (_disposed) return;
            _fullRecapturePending = true;
            ScheduleFlush();
        }
    }

    // Caller holds _pendingLock
    private void ScheduleFlush()
    {
        var now = Environment.TickCount64;
        bool firstPending = _firstPendingTick == 0;
        if (firstPending)
            _firstPendingTick = now;

        var waited = now - _firstPendingTick;
        var due = Math.Max(0, Math.Min(StructureDebounceMs, StructureMaxWaitMs - waited));
        _debounceTimer.Change(due, Timeout.Infinite);
    }

    private void ClearPendingChanges()
    {
        lock (_pendingLock)
        {
            _pendingChanges.Clear();
            _fullRecapturePending = false;
            _firstPendingTick = 0;
        }
    }

    // Whole-document re-captures that failed in a row (UIA thread only)
    private int _fullRecaptureFailures;
    private const int MaxFullRecaptureRetries = 2;

    private void ProcessPendingChanges()
    {
        _ = RunOnUiaThread(() =>
        {
            List<int[]> changes;
            bool full;
            lock (_pendingLock)
            {
                changes = _pendingChanges.Values.ToList();
                full = _fullRecapturePending;
                _pendingChanges.Clear();
                _fullRecapturePending = false;
                _firstPendingTick = 0;
            }

            var root = _documentRoot;
            var rootId = _documentRuntimeId;
            if (root is null || rootId is null || (changes.Count == 0 && !full))
                return;

            // Many changes, or a change to the document itself: re-capture the whole document
            if (full || changes.Count > FullRecaptureThreshold || changes.Any(c => c.AsSpan().SequenceEqual(rootId)))
            {
                UIAElementSnapshot rootSnapshot;
                try
                {
                    var cached = _uiaProvider.WithDocumentCaptureTimeout(() => root.BuildUpdatedCache(_uiaProvider.SubtreeCacheRequest));
                    rootSnapshot = UIAElementSnapshot.Capture(cached);
                }
                catch (Exception ex)
                {
                    // A busy page or a timeout: the pending changes were already taken, so without
                    // a retry the buffer would stay stale until the page happened to change again
                    _logger.LogDebug(ex, "Could not re-capture the document");
                    if (++_fullRecaptureFailures <= MaxFullRecaptureRetries)
                        RequestRecapture(null);
                    return;
                }
                _fullRecaptureFailures = 0;
                RememberWholeCapture(rootSnapshot); // forgets elements removed since
                _eventSink.Post(new SubtreeChangedEvent(DateTimeOffset.UtcNow, rootId, rootSnapshot, rootId));
                return;
            }

            var walker = _uiaProvider.Automation.ControlViewWalker;
            bool recaptureRequested = false;
            foreach (var runtimeId in changes)
            {
                // One element failing (a timeout, a page mid-update) must not drop the rest of the
                // batch; the document is re-captured instead so the change isn't lost
                IUIAutomationElement? element;
                try
                {
                    element = FindInDocument(root, runtimeId, _uiaProvider.SubtreeCacheRequest);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not look up a changed element; re-capturing the document");
                    if (!recaptureRequested)
                    {
                        recaptureRequested = true;
                        RequestRecapture(null);
                    }
                    continue;
                }

                if (element is null)
                {
                    // The element is gone
                    _eventSink.Post(new SubtreeChangedEvent(DateTimeOffset.UtcNow, runtimeId, null, rootId));
                    continue;
                }

                // Raw-view containers (e.g. Chromium generic divs) are not in the buffer: capture
                // the nearest control-view element instead, which the buffer does contain
                var normalized = TryGet(() => walker.NormalizeElementBuildCache(element, _uiaProvider.SubtreeCacheRequest)) ?? element;
                var snapshot = UIAElementSnapshot.Capture(normalized);
                RememberIds(snapshot);
                LimitCapturedIds();
                var changedId = snapshot.RuntimeId.Length > 0 ? snapshot.RuntimeId : runtimeId;

                // Ancestors let the buffer splice an element it doesn't know yet (e.g. added
                // since the last capture) at its nearest known ancestor
                var ancestors = AncestorIds(walker, normalized, rootId);
                _eventSink.Post(new SubtreeChangedEvent(DateTimeOffset.UtcNow, changedId, snapshot, rootId, ancestors));
            }
        }, "updating virtual buffer");
    }

    // Runtime ids of every element captured from the current document (UIA thread only). Ids of
    // removed elements linger harmlessly: they can't receive focus.
    private readonly HashSet<string> _capturedIds = new();

    private static string RuntimeIdKey(int[] runtimeId) => string.Join(",", runtimeId);

    private void RememberIds(IVBufferElement root)
    {
        var stack = new Stack<IVBufferElement>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var element = stack.Pop();
            if (element.RuntimeId.Length > 0)
                _capturedIds.Add(RuntimeIdKey(element.RuntimeId));
            foreach (var child in element.GetChildren())
                stack.Push(child);
        }
    }

    // Size of the last whole capture: the set may grow to a multiple of it with subtree updates
    private int _lastWholeCaptureCount;
    private const int CapturedIdsGrowthLimit = 4;
    private const int MinCapturedIdsLimit = 10_000;

    /// <summary>
    /// On a long-lived changing page (chat, feeds) subtree captures keep adding ids of elements
    /// that are later removed. Past a limit the set is cleared: focus changes then fall back to the
    /// ancestor walk until the next whole capture refills it, which is still correct.
    /// </summary>
    private void LimitCapturedIds()
    {
        int limit = Math.Max(MinCapturedIdsLimit, _lastWholeCaptureCount * CapturedIdsGrowthLimit);
        if (_capturedIds.Count > limit)
            _capturedIds.Clear();
    }

    /// <summary>Records the ids of a whole-document capture, replacing what was known.</summary>
    private void RememberWholeCapture(IVBufferElement root)
    {
        _capturedIds.Clear();
        RememberIds(root);
        _lastWholeCaptureCount = _capturedIds.Count;
    }

    private const int MaxAncestorIds = 16;

    /// <summary>Runtime ids of <paramref name="element"/>'s control-view ancestors up to the document, nearest first.</summary>
    private static List<int[]> AncestorIds(IUIAutomationTreeWalker walker, IUIAutomationElement element, int[] rootId)
    {
        var ids = new List<int[]>();
        var current = element;
        for (int i = 0; i < MaxAncestorIds; i++)
        {
            current = TryGet(() => walker.GetParentElement(current));
            if (current is null)
                break;
            var id = UIAEventSubscriber.TryGetRuntimeId(current);
            if (id.Length == 0)
                break;
            ids.Add(id);
            if (id.AsSpan().SequenceEqual(rootId))
                break;
        }
        return ids;
    }

    /// <summary>
    /// Finds one element of the document by runtime id. This searches the whole document, so on a
    /// large page it gets the long document-capture timeout, like a whole capture.
    /// </summary>
    private IUIAutomationElement? FindInDocument(
        IUIAutomationElement root, int[] runtimeId, IUIAutomationCacheRequest cacheRequest)
    {
        var condition = _uiaProvider.Automation.CreatePropertyCondition(UIAProvider.UIA_RuntimeIdPropertyId, runtimeId);
        return _uiaProvider.WithDocumentCaptureTimeout(
            () => root.FindFirstBuildCache(TreeScope.TreeScope_Descendants, condition, cacheRequest));
    }

    // -------------------------------------------------------------------------
    // Activation
    // -------------------------------------------------------------------------

    public Task<bool> ActivateAsync(VBufferNode node)
    {
        return _uiaThread.RunAsync(() =>
        {
            var root = _documentRoot;
            if (root is null) return false;

            var element = _documentRuntimeId is not null && node.UIARuntimeId.AsSpan().SequenceEqual(_documentRuntimeId)
                ? root
                : FindInDocument(root, node.UIARuntimeId, _uiaProvider.CacheRequest);
            if (element is null) return false;

            // Text entry: put the caret there (NavigationManager has switched to Focus mode)
            if (NavigationManager.IsEditField(node))
            {
                element.SetFocus();
                return true;
            }

            // Its pattern; else a click (a click handler with no accessible action); else focus
            var outcome = ElementActivation.Activate(UIANavigatorObject.For(element, _uiaProvider), _mouse);
            if (outcome == ActivationOutcome.None)
                element.SetFocus();
            return true;
        }, UIAThread.DocumentTimeout); // may search the whole document
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task RunOnUiaThread(Action action, string what)
    {
        try
        {
            // Captures and searches whole documents, which UIA itself allows a long time
            await _uiaThread.RunAsync(action, UIAThread.DocumentTimeout).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Shutting down
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "UIA error while {What}", what);
        }
    }

    /// <summary>How many elements the first, partial capture of a page aims for.</summary>
    internal const int StagedElementTarget = 200;

    /// <summary>How far above the focused element the partial capture starts.</summary>
    private const int FocusRegionLevels = 3;

    /// <summary>
    /// Captures the part of the page around the focused element (a few levels above it), or else
    /// the first top-level parts of the page up to about <see cref="StagedElementTarget"/> elements,
    /// and posts it as a partial document (<see cref="DocumentChangedEvent.IsPartial"/>). The whole
    /// page follows. Nothing is posted when that part is the whole page anyway, or on any failure.
    /// </summary>
    private void PostPartialDocument(IUIAutomationElement document, int[] documentId, int[] focusedId, IUIAutomationElement? focused)
    {
        try
        {
            var walker = _uiaProvider.Automation.ControlViewWalker;
            var request = _uiaProvider.SubtreeCacheRequest;
            var elementOnly = request.Clone();
            elementOnly.TreeScope = TreeScope.TreeScope_Element;

            var parts = new List<UIAElementSnapshot>();
            if (focused is not null && FocusRegion(walker, focused, documentId) is { } region)
            {
                parts.Add(UIAElementSnapshot.Capture(region.BuildUpdatedCache(request)));
            }
            else
            {
                int total = 0;
                var child = TryGet(() => walker.GetFirstChildElement(document));
                while (child is not null && total < StagedElementTarget)
                {
                    var part = UIAElementSnapshot.Capture(child.BuildUpdatedCache(request));
                    parts.Add(part);
                    total += part.CountElements();
                    var current = child;
                    child = TryGet(() => walker.GetNextSiblingElement(current));
                }
                // The first parts were the whole page: the full capture adds nothing
                if (child is null)
                    return;
            }
            if (parts.Count == 0)
                return;

            var root = UIAElementSnapshot.Capture(document.BuildUpdatedCache(elementOnly));
            var partial = new VBufferBuilder { ScreenLayout = _settings?.CurrentValue.ScreenLayout ?? true }
                .Build(UIAElementSnapshot.WithParts(root, parts));
            _logger.LogDebug("Partial buffer posted: {Nodes} nodes", partial.AllNodes.Count);
            _eventSink.Post(new DocumentChangedEvent(DateTimeOffset.UtcNow, partial, focusedId, ProcessNameOf(document), IsPartial: true));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not capture the first part of the page");
        }
    }

    /// <summary>
    /// The ancestor <see cref="FocusRegionLevels"/> levels above the focused element, or the
    /// highest one below the document; null when the focus is the document or right below it.
    /// </summary>
    private static IUIAutomationElement? FocusRegion(IUIAutomationTreeWalker walker, IUIAutomationElement focused, int[] documentId)
    {
        var chain = new List<IUIAutomationElement>();
        var element = focused;
        for (int i = 0; element is not null && i <= MaxAncestorDepth; i++)
        {
            if (UIAEventSubscriber.TryGetRuntimeId(element).AsSpan().SequenceEqual(documentId))
                break;
            chain.Add(element);
            var current = element;
            element = TryGet(() => walker.GetParentElement(current));
        }
        // Never reached the document: the focus isn't in it
        if (element is null || chain.Count < 2)
            return null;
        // The region must leave out part of the page, so not the document's own child
        return chain[Math.Min(FocusRegionLevels, chain.Count - 2)];
    }

    private static string? ProcessNameOf(IUIAutomationElement element)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(element.CachedProcessId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static T? TryGet<T>(Func<T> getter)
    {
        try { return getter(); }
        catch { return default; }
    }

    public void Dispose()
    {
        lock (_pendingLock)
        {
            _disposed = true;
            _pendingChanges.Clear();
        }
        _debounceTimer.Dispose();
    }
}
