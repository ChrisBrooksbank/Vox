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
    public int RowSpan { get; init; } = 1;
    public int ColumnSpan { get; init; } = 1;

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

    private static UIAElementSnapshot CaptureOne(IUIAutomationElement element) => new()
    {
        RuntimeId = UIAEventSubscriber.TryGetRuntimeId(element),
        Name = Try(() => element.CachedName) ?? string.Empty,
        ControlType = UIAEventSubscriber.ControlTypeIdToName(Try(() => element.CachedControlType)),
        AriaRole = Try(() => element.CachedAriaRole) ?? string.Empty,
        AriaProperties = Try(() => element.CachedAriaProperties) ?? string.Empty,
        IsFocusable = Try(() => element.CachedIsKeyboardFocusable != 0),
        HeadingLevel = ReadHeadingLevel(element),
        ExpandCollapseState = ReadCachedInt(element, UIAProvider.UIA_ExpandCollapseStatePropertyId),
        ToggleState = ReadCachedInt(element, UIAProvider.UIA_ToggleStatePropertyId),
        IsSelected = ReadCachedBool(element, UIAProvider.UIA_SelectionItemIsSelectedPropertyId),
        Value = ReadCachedString(element, UIAProvider.UIA_ValueValuePropertyId) ?? string.Empty,
        IsPassword = Try(() => element.CachedIsPassword != 0),
        IsVisited = ReadIsVisited(element),
        IsRequired = ReadCachedBool(element, UIAProvider.UIA_IsRequiredForFormPropertyId) == true,
        Language = LanguageName(ReadCachedInt(element, UIAProvider.UIA_CulturePropertyId)),
        RowSpan = ReadCachedInt(element, UIAProvider.UIA_GridItemRowSpanPropertyId) ?? 1,
        ColumnSpan = ReadCachedInt(element, UIAProvider.UIA_GridItemColumnSpanPropertyId) ?? 1,
    };

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
