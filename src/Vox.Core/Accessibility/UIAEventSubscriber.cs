using Interop.UIAutomationClient;
using Microsoft.Extensions.Logging;
using Vox.Core.Pipeline;

namespace Vox.Core.Accessibility;

/// <summary>
/// Subscribes to UIA events and posts them to the event pipeline.
/// All handler methods fire on UIA's background thread — they only post to channel and return immediately.
///
/// Focus, live region and notification events are desktop-wide. StructureChanged and
/// PropertyChanged are scoped to the active web document (<see cref="SetDocumentScope"/>),
/// since desktop-wide subscriptions to them are very noisy.
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
    private const int UIA_SelectionItem_ElementSelectedEventId = 20012;
    private const int UIA_AsyncContentLoadedEventId = 20023;
    private const int UIA_Text_TextSelectionChangedEventId = 20014;
    private const int UIA_Text_TextChangedEventId = 20015;

    // UIA property IDs for PropertyChanged subscriptions
    private const int UIA_NamePropertyId = 30005;
    private const int UIA_ExpandCollapseStatePropertyId = 30070;
    private const int UIA_ValueValuePropertyId = 30045;

    private readonly UIAThread _uiaThread;
    private readonly UIAProvider _uiaProvider;
    private readonly IEventSink _eventSink;
    private readonly ILogger<UIAEventSubscriber> _logger;

    private bool _subscribed;
    private bool _disposed;

    // Element the structure/property handlers are registered on (STA thread only)
    private IUIAutomationElement? _documentScope;

    // Focused element with a text pattern that the caret/text handlers are registered on (STA thread only)
    private IUIAutomationElement? _textScope;

    // Focused element with text: the one above, or a value-only edit control (STA thread only)
    private IUIAutomationElement? _focusedText;
    private volatile FocusedTextKind _focusedTextKind;

    /// <summary>
    /// The focused element with text (see <see cref="FocusedTextKind"/>), or null. STA thread only.
    /// </summary>
    public IUIAutomationElement? FocusedTextElement => _focusedText;

    /// <summary>What kind of text the focused element has. Readable from any thread.</summary>
    public FocusedTextKind FocusedTextKind => _focusedTextKind;

    private volatile bool _focusedIsTerminal;

    /// <summary>Whether the focused text element is a terminal. Readable from any thread.</summary>
    public bool FocusedIsTerminal => _focusedIsTerminal;

    /// <summary>UIA class names of terminal text areas: Windows Terminal, and the classic console.</summary>
    public static readonly IReadOnlySet<string> TerminalClassNames =
        new HashSet<string>(StringComparer.Ordinal) { "TermControl", "TermControl2", "ConsoleWindowClass" };

    private const int UIA_ValuePatternId = 10002;
    private const int UIA_EditControlTypeId = 50004;

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

            // StructureChanged and PropertyChanged are registered per document (SetDocumentScope)

            // LiveRegionChanged (event 20024) — desktop scope; the cache request brings the
            // region's subtree names so the handler never makes cross-process calls
            automation.AddAutomationEventHandler(
                UIA_LiveRegionChangedEventId,
                automation.GetRootElement(),
                TreeScope.TreeScope_Subtree,
                _uiaProvider.LiveRegionCacheRequest,
                this);

            // Progress bars — desktop scope; the handler keeps only ProgressBar senders
            automation.AddPropertyChangedEventHandler(
                automation.GetRootElement(),
                TreeScope.TreeScope_Subtree,
                _uiaProvider.ProgressCacheRequest,
                this,
                [UIA_RangeValueValuePropertyId]);

            // Notification event (IUIAutomation5) — desktop scope
            if (automation is IUIAutomation5 automation5)
            {
                // The cache request brings the sender's process id, to ignore background apps
                automation5.AddNotificationEventHandler(
                    automation5.GetRootElement(),
                    TreeScope.TreeScope_Subtree,
                    _uiaProvider.CacheRequest,
                    this);
            }
            else
            {
                _logger.LogDebug("IUIAutomation5 not available; Notification events not subscribed");
            }

            _subscribed = true;
            _logger.LogDebug("UIAEventSubscriber: subscribed to all UIA events");
        }, UIAThread.SetupTimeout);
    }

    /// <summary>
    /// Subscribes again with the provider's new automation object after the UIA thread was
    /// replaced (see <see cref="UIAProvider.ReinitializeAsync"/>). The document scope is cleared;
    /// the document tracker sets it again when it reloads the document.
    /// </summary>
    public async Task ResubscribeAsync()
    {
        if (_disposed) return;
        await _uiaThread.RunAsync(() =>
        {
            _documentScope = null;
            _textScope = null;
            _propertyScope = null;
            _selectionScope = null;
            _focusedText = null;
            _focusedTextKind = FocusedTextKind.None;
            _subscribed = false;
        }, UIAThread.SetupTimeout).ConfigureAwait(false);
        await SubscribeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Moves the StructureChanged/PropertyChanged subscriptions to <paramref name="documentRoot"/>
    /// (or removes them when null). Must be called on the UIA STA thread.
    /// </summary>
    public void SetDocumentScope(IUIAutomationElement? documentRoot)
    {
        if (!_subscribed || _disposed) return;

        var automation = _uiaProvider.Automation;

        if (_documentScope is not null)
        {
            try
            {
                automation.RemoveStructureChangedEventHandler(_documentScope, this);
                automation.RemovePropertyChangedEventHandler(_documentScope, this);
                automation.RemoveAutomationEventHandler(UIA_SelectionItem_ElementSelectedEventId, _documentScope, this);
                automation.RemoveAutomationEventHandler(UIA_AsyncContentLoadedEventId, _documentScope, this);
            }
            catch (Exception ex)
            {
                // The old document may already be gone
                _logger.LogDebug(ex, "Error removing document-scoped UIA handlers");
            }
            _documentScope = null;
        }

        if (documentRoot is null) return;

        try
        {
            automation.AddStructureChangedEventHandler(
                documentRoot, TreeScope.TreeScope_Subtree, null, this);
            automation.AddPropertyChangedEventHandler(
                documentRoot, TreeScope.TreeScope_Subtree, null, this,
                new[]
                {
                    UIA_NamePropertyId, UIA_ExpandCollapseStatePropertyId, UIA_ValueValuePropertyId,
                    UIAProvider.UIA_ToggleStatePropertyId, UIAProvider.UIA_SelectionItemIsSelectedPropertyId,
                });
            // Selection changes that don't move focus (e.g. list items, collapsed combo boxes)
            automation.AddAutomationEventHandler(
                UIA_SelectionItem_ElementSelectedEventId, documentRoot, TreeScope.TreeScope_Subtree,
                _uiaProvider.CacheRequest, this);
            // Content finished loading: the document is re-captured
            automation.AddAutomationEventHandler(
                UIA_AsyncContentLoadedEventId, documentRoot, TreeScope.TreeScope_Element, null, this);
            _documentScope = documentRoot;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to subscribe to document structure events");
        }
    }

    /// <summary>
    /// Moves the caret (TextSelectionChanged) and text (TextChanged) subscriptions to the focused
    /// element if it has a text pattern, or removes them. Handlers can't be registered from inside
    /// an event callback, so focus changes call this, and it runs on the UIA thread.
    /// </summary>
    public Task FollowFocusForTextAsync()
    {
        if (!_subscribed || _disposed) return Task.CompletedTask;
        return _uiaThread.RunAsync(() =>
        {
            var automation = _uiaProvider.Automation;
            var focused = automation.GetFocusedElementBuildCache(_uiaProvider.CacheRequest);
            FollowFocusForProperties(automation, focused);
            if (_focusedText is not null && focused is not null && automation.CompareElements(_focusedText, focused) != 0)
                return; // still the same element

            RemoveTextScope(automation);
            if (focused is null)
                return;
            if (UIATextDocument.TryCreate(focused) is null)
            {
                // An edit control with only a value: its caret is read through Win32 after caret keys
                bool isEdit = TryGetValue(focused, () => focused.CachedControlType) == UIA_EditControlTypeId;
                if (isEdit && TryGetValue(focused, () => focused.GetCurrentPattern(UIA_ValuePatternId) is not null))
                {
                    _focusedText = focused;
                    _focusedTextKind = FocusedTextKind.ValueOnly;
                }
                return;
            }

            try
            {
                automation.AddAutomationEventHandler(
                    UIA_Text_TextSelectionChangedEventId, focused, TreeScope.TreeScope_Element, null, this);
                automation.AddAutomationEventHandler(
                    UIA_Text_TextChangedEventId, focused, TreeScope.TreeScope_Element, null, this);
                _textScope = focused;
                _focusedText = focused;
                _focusedTextKind = FocusedTextKind.TextPattern;
                _focusedIsTerminal = TerminalClassNames.Contains(
                    TryGetCachedString(focused, () => focused.CachedClassName) ?? string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not subscribe to caret and text changes of the focused element");
            }
        });
    }

    // Focused element the property handler is registered on, outside documents too (STA thread only)
    private IUIAutomationElement? _propertyScope;

    /// <summary>Property changes reported for the focused element itself (state, value, name, enabled).</summary>
    internal static readonly int[] FocusedElementProperties =
    [
        UIA_NamePropertyId, UIA_ExpandCollapseStatePropertyId, UIA_ValueValuePropertyId,
        UIAProvider.UIA_ToggleStatePropertyId, UIAProvider.UIA_SelectionItemIsSelectedPropertyId,
        UIA_RangeValueValuePropertyId, UIA_IsEnabledPropertyId,
    ];

    private const int UIA_RangeValueValuePropertyId = 30047;
    private const int UIA_ProgressBarControlTypeId = 50012;
    private const int UIA_IsEnabledPropertyId = 30010;

    /// <summary>
    /// Moves the element-scoped property handler to the focused element, so state and value changes
    /// of desktop controls are reported (inside web documents the document-scoped handler also
    /// reports them; BrowseModeController drops the duplicates).
    /// </summary>
    private void FollowFocusForProperties(IUIAutomation automation, IUIAutomationElement? focused)
    {
        if (_propertyScope is not null && focused is not null && automation.CompareElements(_propertyScope, focused) != 0)
            return;

        if (_propertyScope is not null)
        {
            try { automation.RemovePropertyChangedEventHandler(_propertyScope, this); }
            catch (Exception ex) { _logger.LogDebug(ex, "Error removing the focused element's property handler"); }
            _propertyScope = null;
        }
        if (focused is null)
        {
            FollowFocusForSelection(automation, null);
            return;
        }
        try
        {
            automation.AddPropertyChangedEventHandler(focused, TreeScope.TreeScope_Element, null, this, FocusedElementProperties);
            _propertyScope = focused;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not subscribe to the focused element's property changes");
        }
        FollowFocusForSelection(automation, focused);
    }

    // Container whose item selections are reported, outside documents too (STA thread only)
    private IUIAutomationElement? _selectionScope;

    private static readonly HashSet<int> ItemControlTypes = [50007 /* ListItem */, 50024 /* TreeItem */, 50019 /* TabItem */, 50029 /* DataItem */];

    /// <summary>
    /// Reports items selected in the focused list, tree, tab list or grid without focus moving to
    /// them: the handler goes on the focused container, or on the container of a focused item.
    /// </summary>
    private void FollowFocusForSelection(IUIAutomation automation, IUIAutomationElement? focused)
    {
        IUIAutomationElement? container = focused;
        if (focused is not null && ItemControlTypes.Contains(TryGetValue(focused, () => focused.CachedControlType)))
        {
            try { container = automation.ControlViewWalker.GetParentElement(focused); }
            catch { container = null; }
        }

        if (_selectionScope is not null && container is not null && automation.CompareElements(_selectionScope, container) != 0)
            return;
        if (_selectionScope is not null)
        {
            try { automation.RemoveAutomationEventHandler(UIA_SelectionItem_ElementSelectedEventId, _selectionScope, this); }
            catch (Exception ex) { _logger.LogDebug(ex, "Error removing the selection handler"); }
            _selectionScope = null;
        }
        if (container is null)
            return;
        try
        {
            automation.AddAutomationEventHandler(UIA_SelectionItem_ElementSelectedEventId, container,
                TreeScope.TreeScope_Subtree, _uiaProvider.CacheRequest, this);
            _selectionScope = container;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not subscribe to selection changes of the focused container");
        }
    }

    private void RemoveTextScope(IUIAutomation automation)
    {
        _focusedText = null;
        _focusedTextKind = FocusedTextKind.None;
        _focusedIsTerminal = false;
        if (_textScope is null) return;
        try
        {
            automation.RemoveAutomationEventHandler(UIA_Text_TextSelectionChangedEventId, _textScope, this);
            automation.RemoveAutomationEventHandler(UIA_Text_TextChangedEventId, _textScope, this);
        }
        catch (Exception ex)
        {
            // The element may already be gone
            _logger.LogDebug(ex, "Error removing caret and text handlers");
        }
        _textScope = null;
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
            var uiaHeadingLevel = UIAElementSnapshot.ReadHeadingLevel(sender);

            var (headingLevel, isLandmark, landmarkType, isLink) = ParseAriaRole(ariaRole, ariaProps);
            if (uiaHeadingLevel > 0)
                headingLevel = Math.Min(uiaHeadingLevel, 6);
            var isVisited = UIAElementSnapshot.ReadIsVisited(sender) || ParseAriaPropertyBool(ariaProps, "visited");
            var isRequired = UIAElementSnapshot.ReadCachedBool(sender, UIAProvider.UIA_IsRequiredForFormPropertyId) == true
                || ParseAriaPropertyBool(ariaProps, "required");
            var (isExpandable, isExpanded) = Vox.Core.Buffer.ControlState.Expansion(
                UIAElementSnapshot.ReadCachedInt(sender, UIAProvider.UIA_ExpandCollapseStatePropertyId),
                ariaProps, controlType);

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
                IsExpandable: isExpandable,
                RuntimeId: TryGetRuntimeId(sender),
                IsPassword: TryGetValue(sender, () => sender.CachedIsPassword != 0),
                ToggleState: UIAElementSnapshot.ReadCachedInt(sender, UIAProvider.UIA_ToggleStatePropertyId),
                IsSelected: UIAElementSnapshot.ReadCachedBool(sender, UIAProvider.UIA_SelectionItemIsSelectedPropertyId),
                Value: UIAElementSnapshot.ReadCachedString(sender, UIAProvider.UIA_ValueValuePropertyId),
                IsValueReadOnly: UIAElementSnapshot.ReadCachedBool(sender, UIAProvider.UIA_ValueIsReadOnlyPropertyId)
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
            // The sender is the element whose children changed (for ChildRemoved too: the parent);
            // the runtimeId argument only identifies the removed child.
            var id = TryGetRuntimeId(sender);
            if (id.Length == 0)
                id = runtimeId is int[] intArray ? intArray : Array.Empty<int>();
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
            if (propertyId == UIA_RangeValueValuePropertyId && newValue is double value
                && TryGetValue(sender, () => sender.CachedControlType) == UIA_ProgressBarControlTypeId)
            {
                _eventSink.Post(new ProgressChangedEvent(
                    DateTimeOffset.UtcNow, runtimeId, value,
                    Minimum: TryGetValue(sender, () => sender.GetCachedPropertyValue(UIAProvider.UIA_RangeValueMinimumPropertyId) is double min ? min : 0),
                    Maximum: TryGetValue(sender, () => sender.GetCachedPropertyValue(UIAProvider.UIA_RangeValueMaximumPropertyId) is double max ? max : 100),
                    ProcessId: TryGetValue(sender, () => sender.CachedProcessId)));
                return;
            }
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
            else if (eventId == UIA_SelectionItem_ElementSelectedEventId)
            {
                _eventSink.Post(new ElementSelectedEvent(
                    Timestamp: DateTimeOffset.UtcNow,
                    RuntimeId: TryGetRuntimeId(sender),
                    Name: TryGetCachedString(sender, () => sender.CachedName) ?? string.Empty));
            }
            else if (eventId == UIA_Text_TextSelectionChangedEventId)
            {
                _eventSink.Post(new CaretMovedEvent(DateTimeOffset.UtcNow, TryGetRuntimeId(sender)));
            }
            else if (eventId == UIA_Text_TextChangedEventId)
            {
                _eventSink.Post(new TextEditedEvent(DateTimeOffset.UtcNow, TryGetRuntimeId(sender)));
            }
            else if (eventId == UIA_AsyncContentLoadedEventId)
            {
                // Treated as a structure change of the document itself (full re-capture)
                var id = TryGetRuntimeId(sender);
                if (id.Length > 0)
                    _eventSink.Post(new StructureChangedEvent(DateTimeOffset.UtcNow, id));
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
        var liveSetting = TryGetValue(sender, () => sender is IUIAutomationElement2 e2 ? (int)e2.CachedLiveSetting : 1);

        // LiveSetting: 0=Off, 1=Polite, 2=Assertive
        var politeness = liveSetting switch
        {
            2 => LiveRegionPoliteness.Assertive,
            0 => LiveRegionPoliteness.Off,
            _ => LiveRegionPoliteness.Polite
        };
        if (politeness == LiveRegionPoliteness.Off)
            return;

        // Live regions in background applications must not talk over the one the user is using
        var senderProcess = TryGetValue(sender, () => sender.CachedProcessId, -1);
        if (!IsForegroundProcess(senderProcess))
            return;

        var text = GetLiveRegionText(sender);

        var runtimeId = TryGetRuntimeId(sender);
        var sourceId = runtimeId.Length > 0 ? string.Join(",", runtimeId) : null;

        // Cleared regions are posted too: the monitor must see the region go empty, or the same
        // message set again afterwards would look like a duplicate
        {
            _eventSink.Post(new LiveRegionChangedEvent(
                Timestamp: DateTimeOffset.UtcNow,
                Text: text ?? string.Empty,
                Politeness: politeness,
                SourceId: sourceId
            ));
        }
    }

    /// <summary>
    /// The region's Name, or — when empty, as is common for Chromium live region containers —
    /// the names of its cached leaf descendants. Only reads the cache (no cross-process calls).
    /// </summary>
    private static string GetLiveRegionText(IUIAutomationElement region)
    {
        var name = TryGetCachedString(region, () => region.CachedName);
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        var parts = new List<string>();
        var stack = new Stack<IUIAutomationElement>();
        stack.Push(region);
        while (stack.Count > 0)
        {
            var element = stack.Pop();
            IUIAutomationElementArray? children;
            try { children = element.GetCachedChildren(); }
            catch { children = null; }

            if (children is null || children.Length == 0)
            {
                if (!ReferenceEquals(element, region))
                {
                    var leafName = TryGetCachedString(element, () => element.CachedName);
                    if (!string.IsNullOrWhiteSpace(leafName))
                        parts.Add(leafName);
                }
                continue;
            }

            // Push in reverse to visit children in document order
            for (int i = children.Length - 1; i >= 0; i--)
                stack.Push(children.GetElement(i));
        }

        return string.Join(" ", parts);
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
            var senderProcess = TryGetValue(sender, () => sender.CachedProcessId, -1);
            _eventSink.Post(new NotificationEvent(
                Timestamp: DateTimeOffset.UtcNow,
                ActivityId: activityId,
                NotificationText: displayString,
                Processing: (int)notificationProcessing,
                IsFromForeground: IsForegroundProcess(senderProcess)
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    /// <summary>True when <paramref name="processId"/> owns the foreground window (or either is unknown).</summary>
    private static bool IsForegroundProcess(int processId)
    {
        var foreground = ForegroundProcessId();
        return processId < 0 || foreground < 0 || processId == foreground;
    }

    /// <summary>Process id of the foreground window (two cheap Win32 calls, no UIA).</summary>
    private static int ForegroundProcessId()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            return (int)pid;
        }
        catch
        {
            return -1;
        }
    }

    internal static int[] TryGetRuntimeId(IUIAutomationElement element)
    {
        try
        {
            var id = element.GetRuntimeId();
            return id is int[] arr ? arr : Array.Empty<int>();
        }
        catch { return Array.Empty<int>(); }
    }

    internal static string ControlTypeIdToName(int controlTypeId) => controlTypeId switch
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
            "heading" => Vox.Core.Buffer.VBufferBuilder.ParseHeadingLevelProperty(ariaProps),
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
                _documentScope = null;
                _logger.LogDebug("UIAEventSubscriber: unsubscribed from all UIA events");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error unsubscribing from UIA events");
            }
        }, UIAThread.SetupTimeout);
    }
}
