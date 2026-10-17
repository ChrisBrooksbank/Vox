using Vox.Core.Buffer;

namespace Vox.Core.Accessibility;

/// <summary>
/// An <see cref="IVBufferElement"/> over an <see cref="IA2Node"/> tree (a Firefox document read through
/// IAccessible2), so the buffer builder sees landmarks, ARIA states, visited links, relations and
/// <c>lang</c> as it does for a Chromium page's UIA snapshot. Pure and COM-free.
/// </summary>
public sealed class IA2ElementSnapshot : IVBufferElement
{
    // MSAA states (low word)
    private const long Selected = 0x2, Pressed = 0x8, Checked = 0x10, Mixed = 0x20, ReadOnly = 0x40,
        Expanded = 0x200, Collapsed = 0x400, Focusable = 0x100000, Selectable = 0x200000,
        Traversed = 0x800000, Protected = 0x20000000;

    // IA2 states (high word)
    private const long InvalidEntry = 0x80L << 32, Required = 0x2000L << 32, Checkable = 0x100000L << 32;

    // MSAA roles
    private const int RoleDocument = 15, RoleGrouping = 20, RoleSeparator = 21, RoleTable = 24, RoleColumnHeader = 25,
        RoleRowHeader = 26, RoleRow = 28, RoleCell = 29, RoleLink = 30, RoleList = 33, RoleListItem = 34,
        RoleOutline = 35, RoleOutlineItem = 36, RolePageTab = 37, RoleGraphic = 40, RoleStaticText = 41, RoleText = 42,
        RolePushButton = 43, RoleCheckButton = 44, RoleRadioButton = 45, RoleComboBox = 46, RoleProgressBar = 48,
        RoleSlider = 51, RoleSpinButton = 52, RoleMenuItem = 12, RoleMenuPopup = 11, RoleToolBar = 22, RolePageTabList = 60;

    // IA2 roles
    private const int RoleHeading = 0x414, RoleToggleButton = 0x42A, RoleCheckMenuItem = 0x403, RoleRadioMenuItem = 0x41F,
        RoleParagraph = 0x41E, RoleSection = 0x424;

    // Object attributes that carry ARIA state, passed on in AriaProperties
    private static readonly string[] AriaAttributes =
        ["current", "sort", "pressed", "invalid", "level", "required", "live", "roledescription", "keyshortcuts"];

    private readonly IA2Node _node;
    private readonly IReadOnlyList<IVBufferElement> _children;

    private IA2ElementSnapshot(IA2Node node, IReadOnlyList<IVBufferElement> children)
    {
        _node = node;
        _children = children;
        ControlType = ControlTypeOf(node);
        AriaRole = AriaRoleOf(node);
        AriaProperties = AriaPropertiesOf(node);
        HeadingLevel = node.Role == RoleHeading || AriaRole == "heading" ? Math.Clamp(IntAttribute(node, "level"), 0, 9) : 0;
    }

    /// <summary>Wraps a captured tree. Iterative, so a deep page can't overflow the stack.</summary>
    public static IA2ElementSnapshot From(IA2Node root)
    {
        var results = new Dictionary<IA2Node, IA2ElementSnapshot>(ReferenceEqualityComparer.Instance);
        var order = new List<IA2Node>();
        var stack = new Stack<IA2Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            order.Add(n);
            foreach (var c in n.Children) stack.Push(c);
        }
        // Children before parents
        for (int i = order.Count - 1; i >= 0; i--)
        {
            var n = order[i];
            var kids = new List<IVBufferElement>(n.Children.Count);
            foreach (var c in n.Children) kids.Add(results[c]);
            results[n] = new IA2ElementSnapshot(n, kids);
        }
        return results[root];
    }

    public int[] RuntimeId => [_node.UniqueId];
    public string Name => _node.Name;
    public string ControlType { get; }
    public string AriaRole { get; }
    public string AriaProperties { get; }
    public int HeadingLevel { get; }
    public bool IsFocusable => Has(Focusable);
    public bool IsPassword => Has(Protected);
    public bool IsVisited => Has(Traversed);
    public bool IsRequired => Has(Required) || _node.Attribute("required") == "true";
    public string Value => IsPassword ? string.Empty : _node.Value;
    public string Description => _node.Description.Trim();
    public bool? IsDataValidForForm => Has(InvalidEntry) ? false : null;
    public IReadOnlyList<int[]> ErrorMessageIds => Ids("errorMessage");
    public IReadOnlyList<int[]> ControlsIds => Ids("controllerFor");
    public bool HasDetails => Ids("details").Count > 0;
    public string RoleDescription => _node.Attribute("roledescription")?.Trim() ?? string.Empty;
    public string AcceleratorKey => _node.Attribute("keyshortcuts")?.Trim() ?? string.Empty;
    public bool? IsSelected => Has(Selectable) ? Has(Selected) : null;
    public bool? IsValueReadOnly => ControlType == "Edit" ? Has(ReadOnly) : null;
    public string AutomationId => _node.Attribute("id") ?? string.Empty;

    public int? ExpandCollapseState => Has(Expanded) ? 1 : Has(Collapsed) ? 0 : null;

    public int? ToggleState
    {
        get
        {
            bool toggle = Has(Checkable) || _node.Role is RoleCheckButton or RoleRadioButton or RoleToggleButton
                or RoleCheckMenuItem or RoleRadioMenuItem
                || (_node.Attribute("pressed") is not null);
            if (!toggle) return null;
            if (Has(Mixed)) return ControlState.ToggleIndeterminate;
            return Has(Checked) || Has(Pressed) || _node.Attribute("pressed") == "true" ? ControlState.ToggleOn : ControlState.ToggleOff;
        }
    }

    /// <summary>The lang attribute (an object attribute, else the first text run's <c>language</c>).</summary>
    public string Language =>
        (_node.Attribute("lang") ?? _node.Attribute("language")
            ?? _node.TextRuns.Select(r => r.Attributes.GetValueOrDefault("language")).FirstOrDefault(l => !string.IsNullOrEmpty(l))
            ?? string.Empty).Trim();

    public IReadOnlyList<IVBufferElement> GetChildren() => _children;

    private bool Has(long state) => (_node.States & state) != 0;

    private IReadOnlyList<int[]> Ids(string relation) =>
        _node.Relations.TryGetValue(relation, out var ids) ? ids.Select(i => new[] { i }).ToList() : [];

    private static int IntAttribute(IA2Node node, string name) =>
        int.TryParse(node.Attribute(name), out var v) ? v : 0;

    /// <summary>The first token of <c>xml-roles</c> (the ARIA role, or an HTML tag-derived role such as "heading"), else empty.</summary>
    internal static string AriaRoleOf(IA2Node node)
    {
        var roles = node.Attribute("xml-roles");
        if (!string.IsNullOrWhiteSpace(roles))
        {
            var first = roles.Split([' ', ';', ','], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (first is not null && !first.StartsWith("text", StringComparison.Ordinal)) return first.ToLowerInvariant();
        }
        return node.Role switch
        {
            RoleHeading => "heading",
            RoleLink => "link",
            _ => string.Empty,
        };
    }

    internal static string AriaPropertiesOf(IA2Node node)
    {
        var parts = new List<string>();
        foreach (var key in AriaAttributes)
            if (node.Attribute(key) is { Length: > 0 } v)
                parts.Add($"{key}={v}");
        return string.Join(';', parts);
    }

    /// <summary>The UIA-style control type name for an IA2 node.</summary>
    internal static string ControlTypeOf(IA2Node node)
    {
        bool editable = (node.States & (0x8L << 32)) != 0;
        return node.Role switch
        {
            RoleDocument => "Document",
            RoleHeading => "Text",
            RoleLink => "Hyperlink",
            RolePushButton or RoleToggleButton => "Button",
            RoleCheckButton => "CheckBox",
            RoleRadioButton => "RadioButton",
            RoleComboBox => "ComboBox",
            RoleText => "Edit",
            RoleStaticText => editable ? "Edit" : "Text",
            RoleList or RoleOutline => node.Role == RoleOutline ? "Tree" : "List",
            RoleListItem => "ListItem",
            RoleOutlineItem => "TreeItem",
            RoleGraphic => "Image",
            RoleTable => "Table",
            RoleRow => "DataItem",
            RoleCell or RoleColumnHeader or RoleRowHeader => "Text",
            RoleSlider => "Slider",
            RoleSpinButton => "Spinner",
            RoleProgressBar => "ProgressBar",
            RoleSeparator => "Separator",
            RolePageTab => "TabItem",
            RolePageTabList => "Tab",
            RoleMenuItem or RoleCheckMenuItem or RoleRadioMenuItem => "MenuItem",
            RoleMenuPopup => "Menu",
            RoleToolBar => "ToolBar",
            RoleGrouping or RoleParagraph or RoleSection => "Group",
            _ => "Group",
        };
    }
}
