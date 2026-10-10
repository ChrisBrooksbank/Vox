using Interop.UIAutomationClient;
using Microsoft.Extensions.Logging;

namespace Vox.Core.Accessibility;

/// <summary>
/// Creates and owns the CUIAutomation COM object on the dedicated STA thread.
/// Provides a shared cache request for batching UIA property reads.
/// All access to UIA objects must go through UIAThread.
/// </summary>
public sealed class UIAProvider : IDisposable
{
    // UIA property IDs (from Windows SDK UIAutomationClient.h)
    private const int UIA_NamePropertyId = 30005;
    private const int UIA_ControlTypePropertyId = 30003;
    private const int UIA_AriaRolePropertyId = 30101;
    private const int UIA_AriaPropertiesPropertyId = 30102;
    private const int UIA_IsEnabledPropertyId = 30010;
    private const int UIA_HasKeyboardFocusPropertyId = 30008;
    private const int UIA_ItemStatusPropertyId = 30026;
    private const int UIA_LiveSettingPropertyId = 30135;
    private const int UIA_ClassNamePropertyId = 30012;
    internal const int UIA_RuntimeIdPropertyId = 30000;
    internal const int UIA_ProcessIdPropertyId = 30002;
    internal const int UIA_IsKeyboardFocusablePropertyId = 30009;
    internal const int UIA_FrameworkIdPropertyId = 30024;
    internal const int UIA_HeadingLevelPropertyId = 30173;
    internal const int UIA_IsPasswordPropertyId = 30019;
    internal const int UIA_ValueValuePropertyId = 30045;
    internal const int UIA_ExpandCollapseStatePropertyId = 30070;
    internal const int UIA_ToggleStatePropertyId = 30086;
    internal const int UIA_SelectionItemIsSelectedPropertyId = 30079;
    internal const int UIA_ValueIsReadOnlyPropertyId = 30046;
    internal const int UIA_IsRequiredForFormPropertyId = 30025;
    internal const int UIA_LegacyIAccessibleStatePropertyId = 30100;
    internal const int UIA_AcceleratorKeyPropertyId = 30006;
    internal const int UIA_AccessKeyPropertyId = 30007;
    internal const int UIA_PositionInSetPropertyId = 30152;
    internal const int UIA_SizeOfSetPropertyId = 30153;
    internal const int UIA_LevelPropertyId = 30154;
    internal const int UIA_CulturePropertyId = 30015;
    internal const int UIA_GridItemRowSpanPropertyId = 30066;
    internal const int UIA_GridItemColumnSpanPropertyId = 30067;
    internal const int UIA_HelpTextPropertyId = 30013;
    internal const int UIA_FullDescriptionPropertyId = 30159;
    internal const int UIA_LocalizedControlTypePropertyId = 30004;
    internal const int UIA_IsDataValidForFormPropertyId = 30103;
    internal const int UIA_ControllerForPropertyId = 30104;
    internal const int UIA_DescribedByPropertyId = 30105;
    internal const int UIA_AnnotationTypesPropertyId = 30155;
    internal const int UIA_IsInvokePatternAvailablePropertyId = 30031;

    internal const uint ConnectionTimeoutMs = 2000;
    internal const uint TransactionTimeoutMs = 4000;
    // Capturing a whole large document is one long cross-process call: allow it more time
    internal const uint DocumentCaptureTimeoutMs = 20000;

    private readonly UIAThread _uiaThread;
    private readonly ILogger<UIAProvider> _logger;

    private IUIAutomation? _automation;
    private IUIAutomationCacheRequest? _cacheRequest;
    private IUIAutomationCacheRequest? _subtreeCacheRequest;
    private IUIAutomationCacheRequest? _liveRegionCacheRequest;
    private IUIAutomationCacheRequest? _progressCacheRequest;

    internal const int UIA_RangeValueMinimumPropertyId = 30049;
    internal const int UIA_RangeValueMaximumPropertyId = 30050;
    private bool _disposed;

    public UIAProvider(UIAThread uiaThread, ILogger<UIAProvider> logger)
    {
        _uiaThread = uiaThread;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the CUIAutomation COM object on the STA thread.
    /// Must be called before any other methods.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _uiaThread.RunAsync(() =>
        {
            _logger.LogDebug("Creating CUIAutomation8 on STA thread");
            _automation = new CUIAutomation8();

            // A hung page must not hold the single UIA thread for UIA's default 20 s transaction
            // timeout: give up after a few seconds and let later work run
            if (_automation is IUIAutomation2 automation2)
            {
                try
                {
                    automation2.ConnectionTimeout = ConnectionTimeoutMs;
                    automation2.TransactionTimeout = TransactionTimeoutMs;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not set UIA timeouts");
                }
            }

            _cacheRequest = _automation.CreateCacheRequest();
            _cacheRequest.AddProperty(UIA_NamePropertyId);
            _cacheRequest.AddProperty(UIA_ControlTypePropertyId);
            _cacheRequest.AddProperty(UIA_AriaRolePropertyId);
            _cacheRequest.AddProperty(UIA_AriaPropertiesPropertyId);
            _cacheRequest.AddProperty(UIA_IsEnabledPropertyId);
            _cacheRequest.AddProperty(UIA_HasKeyboardFocusPropertyId);
            _cacheRequest.AddProperty(UIA_ItemStatusPropertyId);
            _cacheRequest.AddProperty(UIA_LiveSettingPropertyId);
            _cacheRequest.AddProperty(UIA_ClassNamePropertyId);
            _cacheRequest.AddProperty(UIA_HeadingLevelPropertyId);
            _cacheRequest.AddProperty(UIA_FrameworkIdPropertyId);
            _cacheRequest.AddProperty(UIA_ProcessIdPropertyId);
            _cacheRequest.AddProperty(UIA_IsPasswordPropertyId);
            _cacheRequest.AddProperty(UIA_PositionInSetPropertyId);
            _cacheRequest.AddProperty(UIA_SizeOfSetPropertyId);
            _cacheRequest.AddProperty(UIA_LevelPropertyId);
            _cacheRequest.AddProperty(UIA_AcceleratorKeyPropertyId);
            _cacheRequest.AddProperty(UIA_AccessKeyPropertyId);
            AddStateProperties(_cacheRequest);

            _subtreeCacheRequest = CreateSubtreeRequest(_automation);
            _liveRegionCacheRequest = CreateLiveRegionRequest(_automation);

            // Progress bars anywhere: their type, process and range come with each change
            _progressCacheRequest = _automation.CreateCacheRequest();
            _progressCacheRequest.AddProperty(UIA_ControlTypePropertyId);
            _progressCacheRequest.AddProperty(UIA_ProcessIdPropertyId);
            _progressCacheRequest.AddProperty(UIA_RangeValueMinimumPropertyId);
            _progressCacheRequest.AddProperty(UIA_RangeValueMaximumPropertyId);

            _logger.LogDebug("UIAProvider initialized with cache requests");
        }, UIAThread.SetupTimeout);
    }

    /// <summary>
    /// Creates a new automation object and cache requests on the (new) UIA thread after
    /// <see cref="UIAThread.ReplaceStuckThread"/>. The old automation object's event handlers are
    /// removed in the background: that call can block on the same unresponsive provider that
    /// stuck the old thread, so nothing waits for it.
    /// </summary>
    public async Task ReinitializeAsync()
    {
        var old = await _uiaThread.RunAsync(() => _automation, UIAThread.SetupTimeout).ConfigureAwait(false);
        await InitializeAsync().ConfigureAwait(false);
        if (old is not null && !ReferenceEquals(old, _automation))
        {
            _ = Task.Run(() =>
            {
                try { old.RemoveAllEventHandlers(); }
                catch (Exception ex) { _logger.LogDebug(ex, "Could not remove the old automation object's event handlers"); }
            });
        }
    }

    /// <summary>
    /// Cache request for capturing a whole subtree (control view) in one cross-process call,
    /// with every property the virtual buffer needs. Used with BuildUpdatedCache / FindFirstBuildCache.
    /// </summary>
    private static IUIAutomationCacheRequest CreateSubtreeRequest(IUIAutomation automation)
    {
        var request = automation.CreateCacheRequest();
        request.AddProperty(UIA_NamePropertyId);
        request.AddProperty(UIA_ControlTypePropertyId);
        request.AddProperty(UIA_AriaRolePropertyId);
        request.AddProperty(UIA_AriaPropertiesPropertyId);
        request.AddProperty(UIA_IsKeyboardFocusablePropertyId);
        request.AddProperty(UIA_HeadingLevelPropertyId);
        request.AddProperty(UIA_IsPasswordPropertyId);
        // The element's language (an HTML lang attribute), as an LCID
        request.AddProperty(UIA_CulturePropertyId);
        // Table cell spans, for the table model
        request.AddProperty(UIA_GridItemRowSpanPropertyId);
        request.AddProperty(UIA_GridItemColumnSpanPropertyId);
        // Shortcut keys (aria-keyshortcuts)
        request.AddProperty(UIA_AcceleratorKeyPropertyId);
        // Annotations (a comment through aria-details, tracked changes)
        request.AddProperty(UIA_AnnotationTypesPropertyId);
        // Chromium offers Invoke on elements with a click handler (to say "clickable")
        request.AddProperty(UIA_IsInvokePatternAvailablePropertyId);
        AddStateProperties(request);
        request.TreeScope = TreeScope.TreeScope_Subtree;
        request.TreeFilter = automation.ControlViewCondition;
        return request;
    }

    /// <summary>
    /// Expand/collapse, toggle (checked) and selection state, the value (and whether it is
    /// editable), required, the legacy state bits (visited links), the description, and the ARIA
    /// states Core-AAM maps to UIA properties: invalid and its error message, details, role description.
    /// </summary>
    private static void AddStateProperties(IUIAutomationCacheRequest request)
    {
        request.AddProperty(UIA_ExpandCollapseStatePropertyId);
        request.AddProperty(UIA_ToggleStatePropertyId);
        request.AddProperty(UIA_SelectionItemIsSelectedPropertyId);
        request.AddProperty(UIA_ValueValuePropertyId);
        request.AddProperty(UIA_ValueIsReadOnlyPropertyId);
        request.AddProperty(UIA_IsRequiredForFormPropertyId);
        request.AddProperty(UIA_LegacyIAccessibleStatePropertyId);
        // The description (aria-description / aria-describedby; a desktop control's help text)
        request.AddProperty(UIA_FullDescriptionPropertyId);
        request.AddProperty(UIA_HelpTextPropertyId);
        // aria-invalid, aria-errormessage (ControllerFor), aria-details (DescribedBy),
        // aria-roledescription (LocalizedControlType)
        request.AddProperty(UIA_IsDataValidForFormPropertyId);
        request.AddProperty(UIA_ControllerForPropertyId);
        request.AddProperty(UIA_DescribedByPropertyId);
        request.AddProperty(UIA_LocalizedControlTypePropertyId);
    }

    /// <summary>
    /// Cache request for LiveRegionChanged handlers: caches the region's subtree names so the
    /// handler can read its text without further cross-process calls.
    /// </summary>
    private static IUIAutomationCacheRequest CreateLiveRegionRequest(IUIAutomation automation)
    {
        var request = automation.CreateCacheRequest();
        request.AddProperty(UIA_NamePropertyId);
        request.AddProperty(UIA_LiveSettingPropertyId);
        request.AddProperty(UIA_ProcessIdPropertyId);
        request.TreeScope = TreeScope.TreeScope_Subtree;
        request.TreeFilter = automation.ControlViewCondition;
        return request;
    }

    /// <summary>
    /// Cache request that captures a subtree for the virtual buffer. Must be used on the STA thread.
    /// </summary>
    public IUIAutomationCacheRequest SubtreeCacheRequest
    {
        get
        {
            if (_subtreeCacheRequest is null)
                throw new InvalidOperationException("UIAProvider not initialized. Call InitializeAsync first.");
            return _subtreeCacheRequest;
        }
    }

    /// <summary>
    /// Cache request for live region events (subtree names). Must be used on the STA thread.
    /// </summary>
    public IUIAutomationCacheRequest LiveRegionCacheRequest
    {
        get
        {
            if (_liveRegionCacheRequest is null)
                throw new InvalidOperationException("UIAProvider not initialized. Call InitializeAsync first.");
            return _liveRegionCacheRequest;
        }
    }

    /// <summary>
    /// Runs <paramref name="capture"/> with the long <see cref="DocumentCaptureTimeoutMs"/>
    /// transaction timeout, restoring the short one afterwards. Must be called on the STA thread
    /// (so nothing else runs meanwhile with the long timeout).
    /// </summary>
    public T WithDocumentCaptureTimeout<T>(Func<T> capture)
    {
        if (Automation is not IUIAutomation2 automation2)
            return capture();

        uint previous;
        try
        {
            previous = automation2.TransactionTimeout;
            automation2.TransactionTimeout = DocumentCaptureTimeoutMs;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not raise the UIA timeout for a document capture");
            return capture();
        }

        try
        {
            return capture();
        }
        finally
        {
            try { automation2.TransactionTimeout = previous; }
            catch (Exception ex) { _logger.LogDebug(ex, "Could not restore the UIA timeout"); }
        }
    }

    /// <summary>Cache request for desktop-wide progress bar changes. Must be used on the STA thread.</summary>
    public IUIAutomationCacheRequest ProgressCacheRequest =>
        _progressCacheRequest ?? throw new InvalidOperationException("UIAProvider not initialized. Call InitializeAsync first.");

    /// <summary>
    /// Gets the UIA automation object. Must be called on the STA thread.
    /// </summary>
    public IUIAutomation Automation
    {
        get
        {
            if (_automation is null)
                throw new InvalidOperationException("UIAProvider not initialized. Call InitializeAsync first.");
            return _automation;
        }
    }

    /// <summary>
    /// Gets the shared cache request pre-configured with standard properties.
    /// Must be used on the STA thread.
    /// </summary>
    public IUIAutomationCacheRequest CacheRequest
    {
        get
        {
            if (_cacheRequest is null)
                throw new InvalidOperationException("UIAProvider not initialized. Call InitializeAsync first.");
            return _cacheRequest;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // COM objects are released on the STA thread
        _ = _uiaThread.RunAsync(() =>
        {
            if (_cacheRequest is not null)
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(_cacheRequest);
                _cacheRequest = null;
            }
            if (_subtreeCacheRequest is not null)
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(_subtreeCacheRequest);
                _subtreeCacheRequest = null;
            }
            if (_progressCacheRequest is not null)
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(_progressCacheRequest);
                _progressCacheRequest = null;
            }
            if (_liveRegionCacheRequest is not null)
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(_liveRegionCacheRequest);
                _liveRegionCacheRequest = null;
            }
            if (_automation is not null)
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(_automation);
                _automation = null;
            }
            _logger.LogDebug("UIAProvider disposed");
        }, UIAThread.SetupTimeout);
    }
}
