using System.Text;

namespace Vox.Core.Buffer;

/// <summary>
/// Abstraction over a UIA element for building virtual buffer nodes.
/// Allows VBufferBuilder to be used with real UIA elements (via UIAElementAdapter)
/// and with mock elements for unit testing.
/// </summary>
public interface IVBufferElement
{
    /// <summary>UIA runtime ID.</summary>
    int[] RuntimeId { get; }

    /// <summary>Accessible name.</summary>
    string Name { get; }

    /// <summary>Control type name (e.g. "Document", "Heading", "Link").</summary>
    string ControlType { get; }

    /// <summary>ARIA role string (e.g. "heading", "navigation", "link").</summary>
    string AriaRole { get; }

    /// <summary>ARIA properties string (e.g. "level=2;required=true").</summary>
    string AriaProperties { get; }

    /// <summary>True if this element can receive keyboard focus.</summary>
    bool IsFocusable { get; }

    /// <summary>Returns child elements in order.</summary>
    IReadOnlyList<IVBufferElement> GetChildren();
}

/// <summary>
/// Builds a <see cref="VBufferDocument"/> from a tree of <see cref="IVBufferElement"/> objects.
///
/// Usage:
/// 1. Find the Document element root (ControlType == "Document")
/// 2. Call <see cref="Build"/> with the root element
///
/// The builder walks the tree depth-first (pre-order), assigns document-order IDs,
/// parses AriaRole/AriaProperties for heading levels, landmark types, link status,
/// and required/expanded/visited state, then assembles FlatText and all indices.
///
/// Target: &lt;500ms for a 1000-element page.
/// </summary>
public sealed class VBufferBuilder
{
    // Landmark roles mapped to human-readable types
    private static readonly Dictionary<string, string> LandmarkRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["banner"]        = "Banner",
        ["complementary"] = "Complementary",
        ["contentinfo"]   = "Content info",
        ["form"]          = "Form",
        ["main"]          = "Main",
        ["navigation"]    = "Navigation",
        ["region"]        = "Region",
        ["search"]        = "Search",
    };

    // Control types that are implicitly form fields even without ARIA markup
    private static readonly HashSet<string> FocusableControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Button", "CheckBox", "ComboBox", "Edit", "Hyperlink",
        "ListItem", "MenuItem", "RadioButton", "Slider", "Spinner",
        "Tab", "TabItem", "TreeItem",
    };

    /// <summary>
    /// Builds a <see cref="VBufferDocument"/> from the given root element.
    /// The root should be the UIA Document element for the web page.
    /// </summary>
    /// <param name="root">Root element of the document tree (typically ControlType == "Document").</param>
    /// <returns>A fully-populated <see cref="VBufferDocument"/>.</returns>
    public VBufferDocument Build(IVBufferElement root)
    {
        var allNodes = new List<VBufferNode>(64);
        var flatText = new StringBuilder(256);
        int nextId = 0;

        VBufferNode? prevInOrder = null;

        // Iterative pre-order DFS using explicit stack (avoids recursion overhead on large pages)
        var stack = new Stack<(IVBufferElement Element, VBufferNode? Parent)>();
        stack.Push((root, null));

        while (stack.Count > 0)
        {
            var (element, parentNode) = stack.Pop();

            var node = BuildNode(element, parentNode, nextId++, flatText);

            allNodes.Add(node);

            // Link doubly-linked document-order list
            if (prevInOrder is not null)
            {
                prevInOrder.NextInOrder = node;
                node.PrevInOrder = prevInOrder;
            }
            prevInOrder = node;

            // Push children in reverse order so we process them left-to-right
            var children = element.GetChildren();
            for (int i = children.Count - 1; i >= 0; i--)
            {
                stack.Push((children[i], node));
            }
        }

        return new VBufferDocument(flatText.ToString(), allNodes[0], allNodes);
    }

    private static VBufferNode BuildNode(
        IVBufferElement element,
        VBufferNode? parent,
        int id,
        StringBuilder flatText)
    {
        var ariaRole = element.AriaRole;
        var ariaProps = element.AriaProperties;

        // Parse heading level
        var headingLevel = ParseHeadingLevel(ariaRole, ariaProps, element.ControlType);

        // Parse landmark type
        var landmarkType = ParseLandmarkType(ariaRole);

        // Parse link status
        var isLink = IsLinkElement(ariaRole, element.ControlType);

        // Parse ARIA properties
        var isVisited  = ParseAriaPropertyBool(ariaProps, "visited");
        var isRequired = ParseAriaPropertyBool(ariaProps, "required");
        var isExpanded = ParseAriaPropertyBool(ariaProps, "expanded");
        var isExpandable = ParseAriaPropertyBool(ariaProps, "haspopup") || isExpanded ||
                           string.Equals(element.ControlType, "ComboBox", StringComparison.OrdinalIgnoreCase);

        // Determine focusability
        var isFocusable = element.IsFocusable ||
                          FocusableControlTypes.Contains(element.ControlType) ||
                          isLink;

        // Compute text contribution: leaf text nodes and named interactive elements
        var textStart = flatText.Length;
        AppendNodeText(element, headingLevel, isLink, flatText);
        var textEnd = flatText.Length;

        var node = new VBufferNode
        {
            Id = id,
            UIARuntimeId = element.RuntimeId,
            Name = element.Name,
            ControlType = element.ControlType,
            AriaRole = ariaRole,
            HeadingLevel = headingLevel,
            LandmarkType = landmarkType,
            IsLink = isLink,
            IsVisited = isVisited,
            IsRequired = isRequired,
            IsExpandable = isExpandable,
            IsExpanded = isExpanded,
            IsFocusable = isFocusable,
            TextRange = (textStart, textEnd),
            Parent = parent,
        };

        parent?.Children.Add(node);

        return node;
    }

    private static void AppendNodeText(
        IVBufferElement element,
        int headingLevel,
        bool isLink,
        StringBuilder flatText)
    {
        var name = element.Name;
        if (string.IsNullOrEmpty(name)) return;

        // Only leaf-like elements contribute text (not container elements like Document/Group/Pane)
        // We include text from: Text, Heading (via ariaRole), Link, Button, Edit, Image (alt text), etc.
        var ct = element.ControlType;
        var isContainer = ct is "Document" or "Group" or "Pane" or "Window" or "Custom"
                          or "ToolBar" or "Menu" or "MenuBar" or "StatusBar" or "TitleBar";

        if (isContainer) return;

        flatText.Append(name);
        flatText.Append('\n');
    }

    // -------------------------------------------------------------------------
    // Parsing helpers (mirrors UIAEventSubscriber helpers)
    // -------------------------------------------------------------------------

    private static int ParseHeadingLevel(string ariaRole, string ariaProps, string controlType)
    {
        if (string.IsNullOrEmpty(ariaRole))
        {
            // Fallback: UIA ControlType "Header" or "HeaderItem" is not a heading — only ARIA signals it
            return 0;
        }

        var role = ariaRole.Trim();
        return role.ToLowerInvariant() switch
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
    }

    private static string ParseLandmarkType(string ariaRole)
    {
        if (string.IsNullOrEmpty(ariaRole)) return string.Empty;

        return LandmarkRoles.TryGetValue(ariaRole.Trim(), out var type) ? type : string.Empty;
    }

    private static bool IsLinkElement(string ariaRole, string controlType)
    {
        if (!string.IsNullOrEmpty(ariaRole))
        {
            var role = ariaRole.Trim().ToLowerInvariant();
            if (role is "link" or "a") return true;
        }
        return string.Equals(controlType, "Hyperlink", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ParseAriaPropertyBool(string ariaProps, string key)
    {
        if (string.IsNullOrEmpty(ariaProps)) return false;

        // Format: "key=value;key2=value2" or "key:value,key2:value2"
        foreach (var segment in ariaProps.Split(';', ','))
        {
            var sep = segment.IndexOf('=');
            if (sep < 0) sep = segment.IndexOf(':');
            if (sep < 0) continue;

            var k = segment[..sep].Trim();
            var v = segment[(sep + 1)..].Trim();

            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                       v == "1" ||
                       v.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public static int ParseAriaPropertyInt(string ariaProps, string key)
    {
        if (string.IsNullOrEmpty(ariaProps)) return 0;

        foreach (var segment in ariaProps.Split(';', ','))
        {
            var sep = segment.IndexOf('=');
            if (sep < 0) sep = segment.IndexOf(':');
            if (sep < 0) continue;

            var k = segment[..sep].Trim();
            var v = segment[(sep + 1)..].Trim();

            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out var result))
                return result;
        }
        return 0;
    }
}
