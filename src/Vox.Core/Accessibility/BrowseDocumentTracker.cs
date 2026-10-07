using Interop.UIAutomationClient;
using Microsoft.Extensions.Logging;
using Vox.Core.Buffer;
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
    private const int UIA_InvokePatternId = 10000;
    private const int UIA_LegacyIAccessiblePatternId = 10018;
    private const int MaxAncestorDepth = 64;
    private const int StructureDebounceMs = 300;
    private const int StructureMaxWaitMs = 1000;
    private const int FullRecaptureThreshold = 20;

    private static readonly HashSet<string> WebFrameworks = new(StringComparer.OrdinalIgnoreCase)
    {
        "Chrome", // Chrome and Edge
        "Gecko",  // Firefox
    };

    private readonly UIAThread _uiaThread;
    private readonly UIAProvider _uiaProvider;
    private readonly UIAEventSubscriber _eventSubscriber;
    private readonly IEventSink _eventSink;
    private readonly ILogger<BrowseDocumentTracker> _logger;
    private readonly int _ownProcessId = Environment.ProcessId;

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
        ILogger<BrowseDocumentTracker> logger)
    {
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

        LoadDocument(document, documentId, focusedId);
    }

    /// <summary>
    /// Walks up from the focused element to the outermost web Document (so iframes resolve to the page).
    /// </summary>
    private static IUIAutomationElement? FindWebDocument(
        IUIAutomation automation, IUIAutomationElement focused, IUIAutomationCacheRequest cacheRequest)
    {
        var walker = automation.ControlViewWalker;
        IUIAutomationElement? found = null;
        var element = focused;

        for (int depth = 0; element is not null && depth < MaxAncestorDepth; depth++)
        {
            var isWeb = WebFrameworks.Contains(TryGet(() => element.CachedFrameworkId) ?? string.Empty);
            if (isWeb && TryGet(() => element.CachedControlType) == UIA_DocumentControlTypeId)
                found = element;
            else if (found is not null && !isWeb)
                break; // Left the browser's web content

            element = TryGet(() => walker.GetParentElementBuildCache(element, cacheRequest));
        }

        return found;
    }

    private void LoadDocument(IUIAutomationElement document, int[] documentId, int[] focusedId)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Subscribe to the document's changes before capturing it, so nothing that changes in
        // between is missed (a duplicate update is harmless; a missed one is not). Changes that
        // arrive during the capture wait in the pending set until the document is recorded.
        ClearPendingChanges();
        _eventSubscriber.SetDocumentScope(document);

        VBufferDocument buffer;
        try
        {
            var cached = _uiaProvider.WithDocumentCaptureTimeout(() => document.BuildUpdatedCache(_uiaProvider.SubtreeCacheRequest));
            var snapshot = UIAElementSnapshot.Capture(cached);
            buffer = new VBufferBuilder().Build(snapshot);
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
        _eventSink.Post(new DocumentChangedEvent(DateTimeOffset.UtcNow, buffer, focusedId));
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

            if (TryGet(() => element.GetCurrentPattern(UIA_InvokePatternId)) is IUIAutomationInvokePattern invoke)
            {
                invoke.Invoke();
                return true;
            }

            if (TryGet(() => element.GetCurrentPattern(UIA_LegacyIAccessiblePatternId)) is IUIAutomationLegacyIAccessiblePattern legacy)
            {
                legacy.DoDefaultAction();
                return true;
            }

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
