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
    public Task OnFocusChangedAsync() => RunOnUiaThread(DetectDocument, "detecting web document");

    private void DetectDocument()
    {
        var automation = _uiaProvider.Automation;
        var cacheRequest = _uiaProvider.CacheRequest;

        var focused = automation.GetFocusedElementBuildCache(cacheRequest);
        if (focused is null) return;

        // Vox's own windows (e.g. the Elements List) must not unload the document
        if (TryGet(() => focused.CachedProcessId) == _ownProcessId)
            return;

        var document = FindWebDocument(automation, focused, cacheRequest);
        if (document is null)
        {
            if (_documentRoot is not null)
                UnloadDocument();
            return;
        }

        var documentId = UIAEventSubscriber.TryGetRuntimeId(document);
        if (_documentRuntimeId is not null && documentId.AsSpan().SequenceEqual(_documentRuntimeId))
            return; // Same document: the cursor follows focus via FocusChangedEvent.RuntimeId

        LoadDocument(document, documentId, UIAEventSubscriber.TryGetRuntimeId(focused));
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
        var cached = document.BuildUpdatedCache(_uiaProvider.SubtreeCacheRequest);
        var snapshot = UIAElementSnapshot.Capture(cached);
        var buffer = new VBufferBuilder().Build(snapshot);

        _documentRoot = document;
        _documentRuntimeId = documentId;
        ClearPendingChanges();
        _eventSubscriber.SetDocumentScope(document);

        _logger.LogInformation("Virtual buffer built: {Nodes} nodes in {Ms}ms", buffer.AllNodes.Count, sw.ElapsedMilliseconds);
        _eventSink.Post(new DocumentChangedEvent(DateTimeOffset.UtcNow, buffer, focusedId));
    }

    private void UnloadDocument()
    {
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
    /// (common while pages load) produce one update per element.
    /// </summary>
    public void OnStructureChanged(int[] runtimeId)
    {
        if (runtimeId.Length == 0) return;

        lock (_pendingLock)
        {
            if (_disposed) return;
            _pendingChanges[string.Join(",", runtimeId)] = runtimeId;
            _debounceTimer.Change(StructureDebounceMs, Timeout.Infinite);
        }
    }

    private void ClearPendingChanges()
    {
        lock (_pendingLock)
        {
            _pendingChanges.Clear();
        }
    }

    private void ProcessPendingChanges()
    {
        _ = RunOnUiaThread(() =>
        {
            List<int[]> changes;
            lock (_pendingLock)
            {
                changes = _pendingChanges.Values.ToList();
                _pendingChanges.Clear();
            }

            var root = _documentRoot;
            var rootId = _documentRuntimeId;
            if (root is null || rootId is null || changes.Count == 0)
                return;

            // Many changes, or a change to the document itself: re-capture the whole document
            if (changes.Count > FullRecaptureThreshold || changes.Any(c => c.AsSpan().SequenceEqual(rootId)))
            {
                var cached = root.BuildUpdatedCache(_uiaProvider.SubtreeCacheRequest);
                _eventSink.Post(new SubtreeChangedEvent(DateTimeOffset.UtcNow, rootId, UIAElementSnapshot.Capture(cached)));
                return;
            }

            foreach (var runtimeId in changes)
            {
                var element = FindInDocument(root, runtimeId, _uiaProvider.SubtreeCacheRequest);
                var snapshot = element is null ? null : UIAElementSnapshot.Capture(element);
                _eventSink.Post(new SubtreeChangedEvent(DateTimeOffset.UtcNow, runtimeId, snapshot));
            }
        }, "updating virtual buffer");
    }

    private IUIAutomationElement? FindInDocument(
        IUIAutomationElement root, int[] runtimeId, IUIAutomationCacheRequest cacheRequest)
    {
        var condition = _uiaProvider.Automation.CreatePropertyCondition(UIAProvider.UIA_RuntimeIdPropertyId, runtimeId);
        return root.FindFirstBuildCache(TreeScope.TreeScope_Descendants, condition, cacheRequest);
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
        });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task RunOnUiaThread(Action action, string what)
    {
        try
        {
            await _uiaThread.RunAsync(action).ConfigureAwait(false);
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
