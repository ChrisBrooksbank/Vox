using Interop.UIAutomationClient;
using Microsoft.Extensions.Logging;
using Vox.Core.Pipeline;

namespace Vox.Core.Accessibility;

/// <summary>
/// Subscribes to UIA events and posts them to the event pipeline.
/// All handler methods fire on UIA's background thread — they only post to channel and return immediately.
/// </summary>
public sealed class UIAEventSubscriber :
    IUIAutomationFocusChangedEventHandler,
    IUIAutomationStructureChangedEventHandler,
    IUIAutomationPropertyChangedEventHandler,
    IUIAutomationEventHandler,
    IUIAutomationNotificationEventHandler,
    IDisposable
{
    // UIA event IDs
    private const int UIA_LiveRegionChangedEventId = 20024;
    private const int UIA_NotificationEventId = 20035;

    // UIA property IDs for PropertyChanged subscriptions
    private const int UIA_NamePropertyId = 30005;
    private const int UIA_ExpandCollapseStatePropertyId = 30070;

    private readonly UIAThread _uiaThread;
    private readonly UIAProvider _uiaProvider;
    private readonly IEventSink _eventSink;
    private readonly ILogger<UIAEventSubscriber> _logger;

    private bool _subscribed;
    private bool _disposed;

    public UIAEventSubscriber(
        UIAThread uiaThread,
        UIAProvider uiaProvider,
        IEventSink eventSink,
        ILogger<UIAEventSubscriber> logger)
    {
        _uiaThread = uiaThread;
        _uiaProvider = uiaProvider;
        _eventSink = eventSink;
        _logger = logger;
    }

    /// <summary>
    /// Subscribes to all UIA events. Must be called after UIAProvider.InitializeAsync().
    /// Runs on the STA thread.
    /// </summary>
    public async Task SubscribeAsync()
    {
        await _uiaThread.RunAsync(() =>
        {
            var automation = _uiaProvider.Automation;

            // FocusChanged — desktop scope
            automation.AddFocusChangedEventHandler(_uiaProvider.CacheRequest, this);

            // StructureChanged — desktop scope
            automation.AddStructureChangedEventHandler(
                automation.GetRootElement(),
                TreeScope.TreeScope_Subtree,
                null,
                this);

            // PropertyChanged — Name and ExpandCollapseState — desktop scope
            var propertyIds = new[] { UIA_NamePropertyId, UIA_ExpandCollapseStatePropertyId };
            automation.AddPropertyChangedEventHandler(
                automation.GetRootElement(),
                TreeScope.TreeScope_Subtree,
                null,
                this,
                propertyIds);

            // LiveRegionChanged (event 20024) — desktop scope
            automation.AddAutomationEventHandler(
                UIA_LiveRegionChangedEventId,
                automation.GetRootElement(),
                TreeScope.TreeScope_Subtree,
                _uiaProvider.CacheRequest,
                this);

            // Notification event (IUIAutomation5) — desktop scope
            if (automation is IUIAutomation5 automation5)
            {
                automation5.AddNotificationEventHandler(
                    automation5.GetRootElement(),
                    TreeScope.TreeScope_Subtree,
                    null,
                    this);
            }
            else
            {
                _logger.LogDebug("IUIAutomation5 not available; Notification events not subscribed");
            }

            _subscribed = true;
            _logger.LogDebug("UIAEventSubscriber: subscribed to all UIA events");
        });
    }

    // -------------------------------------------------------------------------
    // IUIAutomationFocusChangedEventHandler
    // -------------------------------------------------------------------------

    void IUIAutomationFocusChangedEventHandler.HandleFocusChangedEvent(IUIAutomationElement sender)
    {
        // Fire-and-forget: only post to channel, return immediately
        try
        {
            var name = TryGetCachedString(sender, () => sender.CachedName) ?? string.Empty;
            var controlTypeId = TryGetValue(sender, () => sender.CachedControlType);
            var controlType = ControlTypeIdToName(controlTypeId);
            var ariaRole = TryGetCachedString(sender, () => sender.CachedAriaRole);
            var ariaProps = TryGetCachedString(sender, () => sender.CachedAriaProperties);
            var liveSetting = TryGetValue(sender, () => sender is IUIAutomationElement2 e2 ? (int)e2.CachedLiveSetting : 0);

            var (headingLevel, isLandmark, landmarkType, isLink) = ParseAriaRole(ariaRole, ariaProps);
            var isVisited = ParseAriaPropertyBool(ariaProps, "visited");
            var isRequired = ParseAriaPropertyBool(ariaProps, "required");
            var isExpanded = ParseAriaPropertyBool(ariaProps, "expanded");
            var isExpandable = ParseAriaPropertyBool(ariaProps, "haspopup") || isExpanded;

            _eventSink.Post(new FocusChangedEvent(
                Timestamp: DateTimeOffset.UtcNow,
                ElementName: name,
                ControlType: controlType,
                AriaRole: ariaRole,
                LandmarkType: landmarkType,
                HeadingLevel: headingLevel,
                IsLink: isLink,
                IsVisited: isVisited,
                IsRequired: isRequired,
                IsExpanded: isExpanded,
                IsExpandable: isExpandable
            ));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error reading UIA element in FocusChanged handler");
            // Still post a minimal focus event rather than nothing
            _eventSink.Post(new FocusChangedEvent(
                Timestamp: DateTimeOffset.UtcNow,
                ElementName: string.Empty,
                ControlType: "Unknown"
            ));
        }
    }

    // -------------------------------------------------------------------------
    // IUIAutomationStructureChangedEventHandler
    // -------------------------------------------------------------------------

    void IUIAutomationStructureChangedEventHandler.HandleStructureChangedEvent(
        IUIAutomationElement sender,
        StructureChangeType changeType,
        int[] runtimeId)
    {
        try
        {
            var id = runtimeId is int[] intArray ? intArray : Array.Empty<int>();
            _eventSink.Post(new StructureChangedEvent(
                Timestamp: DateTimeOffset.UtcNow,
                RuntimeId: id
            ));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error in StructureChanged handler");
        }
    }

    // -------------------------------------------------------------------------
    // IUIAutomationPropertyChangedEventHandler
    // -------------------------------------------------------------------------

    void IUIAutomationPropertyChangedEventHandler.HandlePropertyChangedEvent(
        IUIAutomationElement sender,
        int propertyId,
        object newValue)
    {
        try
        {
            var runtimeId = TryGetRuntimeId(sender);
            _eventSink.Post(new PropertyChangedEvent(
                Timestamp: DateTimeOffset.UtcNow,
                RuntimeId: runtimeId,
                PropertyId: propertyId,
                NewValue: newValue
            ));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error in PropertyChanged handler");
        }
    }

    // -------------------------------------------------------------------------
    // IUIAutomationEventHandler — used for LiveRegionChanged and Notification
    // -------------------------------------------------------------------------

    void IUIAutomationEventHandler.HandleAutomationEvent(IUIAutomationElement sender, int eventId)
    {
        try
        {
            if (eventId == UIA_LiveRegionChangedEventId)
            {
                HandleLiveRegionChanged(sender);
            }
            else if (eventId == UIA_NotificationEventId)
            {
                // Notification events are handled via IUIAutomationNotificationEventHandler
                // This path shouldn't be reached for notification events normally
                _eventSink.Post(new NotificationEvent(
                    Timestamp: DateTimeOffset.UtcNow,
                    ActivityId: null,
                    NotificationText: null
                ));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error in AutomationEvent handler (eventId={EventId})", eventId);
        }
    }

    private void HandleLiveRegionChanged(IUIAutomationElement sender)
    {
        var name = TryGetCachedString(sender, () => sender.CachedName) ?? string.Empty;
        var liveSetting = TryGetValue(sender, () => sender is IUIAutomationElement2 e2 ? (int)e2.CachedLiveSetting : 0);
        var ariaProps = TryGetCachedString(sender, () => sender.CachedAriaProperties);

        // LiveSetting: 0=Off, 1=Polite, 2=Assertive
        var politeness = liveSetting switch
        {
            2 => LiveRegionPoliteness.Assertive,
            1 => LiveRegionPoliteness.Polite,
            _ => LiveRegionPoliteness.Polite
        };

        var runtimeId = TryGetRuntimeId(sender);
        var sourceId = runtimeId.Length > 0 ? string.Join(",", runtimeId) : null;

        if (!string.IsNullOrWhiteSpace(name))
        {
            _eventSink.Post(new LiveRegionChangedEvent(
                Timestamp: DateTimeOffset.UtcNow,
                Text: name,
                Politeness: politeness,
                SourceId: sourceId
            ));
        }
    }

    // -------------------------------------------------------------------------
    // IUIAutomationNotificationEventHandler — for IUIAutomation5 notification events
    // -------------------------------------------------------------------------

    void IUIAutomationNotificationEventHandler.HandleNotificationEvent(
        IUIAutomationElement sender,
        NotificationKind notificationKind,
        NotificationProcessing notificationProcessing,
        string displayString,
        string activityId)
    {
        try
        {
            _eventSink.Post(new NotificationEvent(
                Timestamp: DateTimeOffset.UtcNow,
                ActivityId: activityId,
                NotificationText: displayString
            ));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error in Notification handler");
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string? TryGetCachedString(IUIAutomationElement element, Func<string> getter)
    {
        try { return getter(); }
        catch { return null; }
    }

    private static T TryGetValue<T>(IUIAutomationElement element, Func<T> getter, T defaultValue = default!)
    {
        try { return getter(); }
        catch { return defaultValue; }
    }

    private static int[] TryGetRuntimeId(IUIAutomationElement element)
    {
        try
        {
            var id = element.GetRuntimeId();
            return id is int[] arr ? arr : Array.Empty<int>();
        }
        catch { return Array.Empty<int>(); }
    }

    private static string ControlTypeIdToName(int controlTypeId) => controlTypeId switch
    {
        50000 => "Button",
        50001 => "Calendar",
        50002 => "CheckBox",
        50003 => "ComboBox",
        50004 => "Edit",
        50005 => "Hyperlink",
        50006 => "Image",
        50007 => "ListItem",
        50008 => "List",
        50009 => "Menu",
        50010 => "MenuBar",
        50011 => "MenuItem",
        50012 => "ProgressBar",
        50013 => "RadioButton",
        50014 => "ScrollBar",
        50015 => "Slider",
        50016 => "Spinner",
        50017 => "StatusBar",
        50018 => "Tab",
        50019 => "TabItem",
        50020 => "Text",
        50021 => "ToolBar",
        50022 => "ToolTip",
        50023 => "Tree",
        50024 => "TreeItem",
        50025 => "Custom",
        50026 => "Group",
        50027 => "Thumb",
        50028 => "DataGrid",
        50029 => "DataItem",
        50030 => "Document",
        50031 => "SplitButton",
        50032 => "Window",
        50033 => "Pane",
        50034 => "Header",
        50035 => "HeaderItem",
        50036 => "Table",
        50037 => "TitleBar",
        50038 => "Separator",
        50039 => "SemanticZoom",
        50040 => "AppBar",
        _ => "Unknown"
    };

    private static (int HeadingLevel, bool IsLandmark, string? LandmarkType, bool IsLink) ParseAriaRole(
        string? ariaRole,
        string? ariaProps)
    {
        if (string.IsNullOrEmpty(ariaRole))
            return (0, false, null, false);

        var role = ariaRole.ToLowerInvariant().Trim();

        var headingLevel = role switch
        {
            "heading" => ParseAriaPropertyInt(ariaProps, "level"),
            "h1" => 1,
            "h2" => 2,
            "h3" => 3,
            "h4" => 4,
            "h5" => 5,
            "h6" => 6,
            _ => 0
        };

        var isLink = role is "link" or "a";

        var (isLandmark, landmarkType) = role switch
        {
            "banner" => (true, "Banner"),
            "complementary" => (true, "Complementary"),
            "contentinfo" => (true, "Content info"),
            "form" => (true, "Form"),
            "main" => (true, "Main"),
            "navigation" => (true, "Navigation"),
            "region" => (true, "Region"),
            "search" => (true, "Search"),
            _ => (false, (string?)null)
        };

        return (headingLevel, isLandmark, landmarkType, isLink);
    }

    private static bool ParseAriaPropertyBool(string? ariaProps, string key)
    {
        if (string.IsNullOrEmpty(ariaProps)) return false;

        // Format: "key=value;key2=value2" or "key:value,key2:value2"
        foreach (var segment in ariaProps.Split(';', ','))
        {
            var sep = segment.IndexOf('=');
            if (sep < 0) sep = segment.IndexOf(':');
            if (sep < 0) continue;

            var k = segment[..sep].Trim().ToLowerInvariant();
            var v = segment[(sep + 1)..].Trim().ToLowerInvariant();

            if (k == key.ToLowerInvariant())
                return v is "true" or "1" or "yes";
        }
        return false;
    }

    private static int ParseAriaPropertyInt(string? ariaProps, string key)
    {
        if (string.IsNullOrEmpty(ariaProps)) return 0;

        foreach (var segment in ariaProps.Split(';', ','))
        {
            var sep = segment.IndexOf('=');
            if (sep < 0) sep = segment.IndexOf(':');
            if (sep < 0) continue;

            var k = segment[..sep].Trim().ToLowerInvariant();
            var v = segment[(sep + 1)..].Trim();

            if (k == key.ToLowerInvariant() && int.TryParse(v, out var result))
                return result;
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_subscribed) return;

        // Unsubscribe all event handlers on the STA thread
        _ = _uiaThread.RunAsync(() =>
        {
            try
            {
                var automation = _uiaProvider.Automation;
                automation.RemoveFocusChangedEventHandler(this);
                automation.RemoveAllEventHandlers();
                _logger.LogDebug("UIAEventSubscriber: unsubscribed from all UIA events");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error unsubscribing from UIA events");
            }
        });
    }
}
