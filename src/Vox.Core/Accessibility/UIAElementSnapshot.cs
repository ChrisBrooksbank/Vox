using Interop.UIAutomationClient;
using Vox.Core.Buffer;

namespace Vox.Core.Accessibility;

/// <summary>
/// Plain managed copy of a cached UIA subtree, implementing <see cref="IVBufferElement"/>.
///
/// <see cref="Capture"/> runs on the UIA STA thread and reads only cached properties
/// (the element must come from BuildUpdatedCache/FindFirstBuildCache with
/// <see cref="UIAProvider.SubtreeCacheRequest"/>). The resulting snapshot holds no COM
/// references, so it can be handed to the pipeline thread and built into a virtual buffer there.
/// </summary>
public sealed class UIAElementSnapshot : IVBufferElement
{
    private const int HeadingLevelNone = 80050; // UIA HeadingLevel_None; HeadingLevel1..9 = 80051..80059

    private readonly List<UIAElementSnapshot> _children = new();

    public int[] RuntimeId { get; init; } = [];
    public string Name { get; init; } = string.Empty;
    public string ControlType { get; init; } = string.Empty;
    public string AriaRole { get; init; } = string.Empty;
    public string AriaProperties { get; init; } = string.Empty;
    public bool IsFocusable { get; init; }
    public int HeadingLevel { get; init; }
    public int? ExpandCollapseState { get; init; }
    public int? ToggleState { get; init; }
    public bool? IsSelected { get; init; }
    public string Value { get; init; } = string.Empty;
    public bool IsPassword { get; init; }
    public bool IsVisited { get; init; }
    public bool IsRequired { get; init; }
    public string Language { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool? IsDataValidForForm { get; init; }
    public IReadOnlyList<int[]> ErrorMessageIds { get; init; } = [];
    public string RoleDescription { get; init; } = string.Empty;
    public string AcceleratorKey { get; init; } = string.Empty;
    public bool HasDetails { get; init; }
    public int RowSpan { get; init; } = 1;
    public int ColumnSpan { get; init; } = 1;
    public IReadOnlyList<int> AnnotationTypes { get; init; } = [];
    public bool IsInvokable { get; init; }
    public bool? IsValueReadOnly { get; init; }

    public IReadOnlyList<IVBufferElement> GetChildren() => _children;

    /// <summary>
    /// Copies a cached UIA subtree. Must be called on the UIA STA thread.
    /// </summary>
    public static UIAElementSnapshot Capture(IUIAutomationElement cachedRoot)
    {
        var root = CaptureOne(cachedRoot);

        // Iterative walk to cope with deep pages
        var stack = new Stack<(IUIAutomationElement Element, UIAElementSnapshot Snapshot)>();
        stack.Push((cachedRoot, root));
        while (stack.Count > 0)
        {
            var (element, snapshot) = stack.Pop();
            IUIAutomationElementArray? children;
            try { children = element.GetCachedChildren(); }
            catch { children = null; }
            if (children is null) continue;

            for (int i = 0; i < children.Length; i++)
            {
                var child = children.GetElement(i);
                var childSnapshot = CaptureOne(child);
                snapshot._children.Add(childSnapshot);
                stack.Push((child, childSnapshot));
            }
        }

        return root;
    }

    /// <summary>
    /// A partial page: <paramref name="root"/> (a copy of the document element alone) holding just
    /// <paramref name="parts"/>, the parts of the page captured first (see staged capture in
    /// <see cref="BrowseDocumentTracker"/>).
    /// </summary>
    internal static UIAElementSnapshot WithParts(UIAElementSnapshot root, IEnumerable<UIAElementSnapshot> parts)
    {
        root._children.Clear();
        root._children.AddRange(parts);
        return root;
    }

    /// <summary>How many elements the snapshot holds, itself included.</summary>
    internal int CountElements()
    {
        int count = 0;
        var stack = new Stack<UIAElementSnapshot>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var snapshot = stack.Pop();
            count++;
            foreach (var child in snapshot._children)
                stack.Push(child);
        }
        return count;
    }

    private static UIAElementSnapshot CaptureOne(IUIAutomationElement element)
    {
        var ariaRole = Try(() => element.CachedAriaRole) ?? string.Empty;
        var ariaProperties = Try(() => element.CachedAriaProperties) ?? string.Empty;
        var controlType = UIAEventSubscriber.ControlTypeIdToName(Try(() => element.CachedControlType));
        var isDataValid = ReadCachedBool(element, UIAProvider.UIA_IsDataValidForFormPropertyId);
        return new()
        {
            RuntimeId = UIAEventSubscriber.TryGetRuntimeId(element),
            Name = Try(() => element.CachedName) ?? string.Empty,
            ControlType = controlType,
            AriaRole = ariaRole,
            AriaProperties = ariaProperties,
            IsFocusable = Try(() => element.CachedIsKeyboardFocusable != 0),
            HeadingLevel = ReadHeadingLevel(element) is > 0 and var level ? level
                : string.IsNullOrEmpty(ariaRole) ? WebContent.HeadingLevelFromLocalizedType(ReadCachedString(element, UIAProvider.UIA_LocalizedControlTypePropertyId)) : 0,
            ExpandCollapseState = ReadCachedInt(element, UIAProvider.UIA_ExpandCollapseStatePropertyId),
            ToggleState = ReadCachedInt(element, UIAProvider.UIA_ToggleStatePropertyId),
            IsSelected = ReadCachedBool(element, UIAProvider.UIA_SelectionItemIsSelectedPropertyId),
            Value = ReadCachedString(element, UIAProvider.UIA_ValueValuePropertyId) ?? string.Empty,
            IsPassword = Try(() => element.CachedIsPassword != 0),
            IsVisited = ReadIsVisited(element),
            IsRequired = ReadCachedBool(element, UIAProvider.UIA_IsRequiredForFormPropertyId) == true,
            Description = ReadDescription(element),
            IsDataValidForForm = isDataValid,
            // The error message only matters (and ControllerFor only names it) while invalid
            ErrorMessageIds = AriaStates.Invalid(isDataValid, ariaProperties).Length > 0 ? ReadErrorMessageIds(element) : [],
            RoleDescription = ReadRoleDescription(element, controlType, ariaRole),
            AcceleratorKey = ReadCachedString(element, UIAProvider.UIA_AcceleratorKeyPropertyId)?.Trim() ?? string.Empty,
            HasDetails = ReadHasDetails(element),
            Language = LanguageName(ReadCachedInt(element, UIAProvider.UIA_CulturePropertyId)),
            RowSpan = ReadCachedInt(element, UIAProvider.UIA_GridItemRowSpanPropertyId) ?? 1,
            ColumnSpan = ReadCachedInt(element, UIAProvider.UIA_GridItemColumnSpanPropertyId) ?? 1,
            AnnotationTypes = ReadCachedIntArray(element, UIAProvider.UIA_AnnotationTypesPropertyId),
            IsInvokable = ReadCachedBool(element, UIAProvider.UIA_IsInvokePatternAvailablePropertyId) == true,
            IsValueReadOnly = ReadCachedBool(element, UIAProvider.UIA_ValueIsReadOnlyPropertyId),
        };
    }

    /// <summary>
    /// Runtime IDs of the cached ControllerFor elements: an invalid field's aria-errormessage
    /// targets (Core-AAM), though possibly also a popup it controls (aria-controls), which
    /// <see cref="VBufferDocument.ErrorMessageOf"/> leaves out. Reads no live properties.
    /// </summary>
    internal static IReadOnlyList<int[]> ReadErrorMessageIds(IUIAutomationElement element)
    {
        var targets = Try(() => element.CachedControllerFor);
        if (targets is null) return [];
        var ids = new List<int[]>();
        int count = Try(() => targets.Length);
        for (int i = 0; i < count; i++)
        {
            var target = Try(() => targets.GetElement(i));
            var id = target is null ? [] : UIAEventSubscriber.TryGetRuntimeId(target);
            if (id.Length > 0)
                ids.Add(id);
        }
        return ids;
    }

    /// <summary>True when the cached DescribedBy property names any element (aria-details, Core-AAM).</summary>
    internal static bool ReadHasDetails(IUIAutomationElement element) =>
        Try(() => element.CachedDescribedBy) is { } details && Try(() => details.Length) > 0;

    /// <summary>The role description (aria-roledescription) from the cached LocalizedControlType.</summary>
    internal static string ReadRoleDescription(IUIAutomationElement element, string controlType, string ariaRole) =>
        RoleDescriptionFrom(ReadCachedString(element, UIAProvider.UIA_LocalizedControlTypePropertyId), controlType, ariaRole);

    // Localized control types UIA, and Chromium for ARIA roles, report when the author set no
    // role description (English), compared with spaces and hyphens removed
    private static readonly HashSet<string> DefaultLocalizedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "appbar", "button", "calendar", "checkbox", "combobox", "custom", "datagrid", "dataitem",
        "document", "edit", "group", "header", "headeritem", "hyperlink", "image", "list", "listitem",
        "menu", "menubar", "menuitem", "pane", "progressbar", "radiobutton", "scrollbar", "semanticzoom",
        "separator", "slider", "spinner", "splitbutton", "statusbar", "tab", "tabitem", "table", "text",
        "thumb", "titlebar", "toolbar", "tooltip", "tree", "treeitem", "window",
        "alert", "alertdialog", "application", "article", "banner", "blockquote", "caption", "cell",
        "code", "columnheader", "complementary", "contentinfo", "contentinformation", "definition",
        "deletion", "details", "dialog", "directory", "disclosuretriangle", "emphasis", "feed", "figure",
        "footer", "form", "generic", "graphic", "grid", "gridcell", "heading", "highlight", "img",
        "insertion", "landmark", "link", "listbox", "log", "main", "mark", "marquee", "math", "meter",
        "navigation", "note", "option", "output", "paragraph", "region", "row", "rowgroup", "rowheader",
        "search", "searchbox", "section", "status", "strong", "subscript", "summary", "superscript",
        "switch", "tablist", "tabpanel", "term", "textbox", "time", "timer", "togglebutton", "treegrid",
        "datepicker", "timepicker", "colorpicker", "spinbutton", "radiogroup", "menuitemcheckbox",
        "menuitemradio", "progressindicator", "levelindicator", "descriptionlist",
    };

    /// <summary>
    /// The author's role description (aria-roledescription), which Chromium reports as the
    /// LocalizedControlType: the localized type when it isn't a name UIA or the browser gives the
    /// role anyway, else empty. Only web elements (with an ARIA role) count, and only with an
    /// English user interface, where those default names are known.
    /// </summary>
    public static string RoleDescriptionFrom(string? localizedControlType, string controlType, string ariaRole)
    {
        var type = localizedControlType?.Trim();
        if (string.IsNullOrEmpty(type) || string.IsNullOrWhiteSpace(ariaRole)
            || System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "en")
            return string.Empty;
        static string Key(string s) => s.Replace(" ", "").Replace("-", "").Replace("_", "");
        var key = Key(type);
        if (DefaultLocalizedTypes.Contains(key)
            || string.Equals(key, Key(controlType), StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, Key(ariaRole.Trim()), StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, Key(Navigation.ControlTypeNames.ToSpoken(controlType) ?? string.Empty), StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return type;
    }

    /// <summary>The BCP 47 name of a UIA Culture (an LCID), or empty when none or unknown.</summary>
    public static string LanguageName(int? lcid)
    {
        // 0: not set; 127: invariant
        if (lcid is not { } id || id == 0 || id == 127)
            return string.Empty;
        try { return System.Globalization.CultureInfo.GetCultureInfo(id).Name; }
        catch (System.Globalization.CultureNotFoundException) { return string.Empty; }
    }

    /// <summary>A cached string property, or null when not supported.</summary>
    internal static string? ReadCachedString(IUIAutomationElement element, int propertyId) =>
        Try(() => element.GetCachedPropertyValue(propertyId)) as string;

    /// <summary>
    /// The cached description: FullDescription (Chromium's aria-description / aria-describedby
    /// text), else HelpText (a desktop control's help or tooltip text); empty when neither is set.
    /// </summary>
    internal static string ReadDescription(IUIAutomationElement element) =>
        DescriptionFrom(
            ReadCachedString(element, UIAProvider.UIA_FullDescriptionPropertyId),
            ReadCachedString(element, UIAProvider.UIA_HelpTextPropertyId));

    /// <summary>The full description when it has text, else the help text, trimmed; empty when neither has.</summary>
    public static string DescriptionFrom(string? fullDescription, string? helpText) =>
        !string.IsNullOrWhiteSpace(fullDescription) ? fullDescription.Trim()
        : !string.IsNullOrWhiteSpace(helpText) ? helpText.Trim()
        : string.Empty;

    private const int STATE_SYSTEM_TRAVERSED = 0x800000;

    /// <summary>Visited link: the cached LegacyIAccessible state has STATE_SYSTEM_TRAVERSED.</summary>
    internal static bool ReadIsVisited(IUIAutomationElement element) =>
        ReadCachedInt(element, UIAProvider.UIA_LegacyIAccessibleStatePropertyId) is { } state
        && IsTraversed(state);

    /// <summary>True when a LegacyIAccessible state value marks a visited link.</summary>
    public static bool IsTraversed(int legacyState) => (legacyState & STATE_SYSTEM_TRAVERSED) != 0;

    /// <summary>
    /// A cached int property, or null when the element doesn't support it (UIA returns its
    /// "not supported" sentinel object instead of an int).
    /// </summary>
    internal static int? ReadCachedInt(IUIAutomationElement element, int propertyId) =>
        Try(() => element.GetCachedPropertyValue(propertyId)) is int value ? value : null;

    /// <summary>A cached int array property (AnnotationTypes), or empty when not supported.</summary>
    internal static int[] ReadCachedIntArray(IUIAutomationElement element, int propertyId) =>
        Try(() => element.GetCachedPropertyValue(propertyId)) is int[] values ? values : [];

    /// <summary>A cached bool property, or null when not supported.</summary>
    internal static bool? ReadCachedBool(IUIAutomationElement element, int propertyId) =>
        Try(() => element.GetCachedPropertyValue(propertyId)) is bool value ? value : null;

    /// <summary>Converts the cached UIA HeadingLevel property to 1-9, or 0 when not a heading.</summary>
    internal static int ReadHeadingLevel(IUIAutomationElement element)
    {
        var value = Try(() => element.GetCachedPropertyValue(UIAProvider.UIA_HeadingLevelPropertyId));
        return HeadingLevelFromUia(value);
    }

    /// <summary>Maps a UIA HeadingLevel value (80050-80059) to 0-9.</summary>
    public static int HeadingLevelFromUia(object? value) =>
        value is int level && level > HeadingLevelNone && level <= HeadingLevelNone + 9
            ? level - HeadingLevelNone
            : 0;

    private static T? Try<T>(Func<T> getter)
    {
        try { return getter(); }
        catch { return default; }
    }
}
