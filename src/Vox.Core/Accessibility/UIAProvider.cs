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

    private readonly UIAThread _uiaThread;
    private readonly ILogger<UIAProvider> _logger;

    private IUIAutomation? _automation;
    private IUIAutomationCacheRequest? _cacheRequest;
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

            _logger.LogDebug("UIAProvider initialized with cache request for 9 properties");
        });
    }

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
            if (_automation is not null)
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(_automation);
                _automation = null;
            }
            _logger.LogDebug("UIAProvider disposed");
        });
    }
}
