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

    /// <summary>
    /// Heading level 1-9 from the UIA HeadingLevel property, or 0 when not exposed.
    /// Takes precedence over ARIA-derived levels.
    /// </summary>
    int HeadingLevel => 0;

    /// <summary>UIA ExpandCollapseState (0-3), or null when the pattern isn't supported.</summary>
    int? ExpandCollapseState => null;

    /// <summary>UIA ToggleState (0 off, 1 on, 2 indeterminate), or null when not a toggle.</summary>
    int? ToggleState => null;

    /// <summary>UIA SelectionItem.IsSelected, or null when the element isn't selectable.</summary>
    bool? IsSelected => null;

    /// <summary>UIA Value.Value (the text in a text box, a combo box's selection, a slider's value).</summary>
    string Value => string.Empty;

    /// <summary>True for password fields, whose value must never be read.</summary>
    bool IsPassword => false;

    /// <summary>True for a visited link (LegacyIAccessible STATE_SYSTEM_TRAVERSED).</summary>
    bool IsVisited => false;

    /// <summary>True for a required form field (UIA IsRequiredForForm).</summary>
    bool IsRequired => false;

    /// <summary>The element's own language (BCP 47, from the lang attribute), or empty when not set.</summary>
    string Language => string.Empty;

    /// <summary>Rows a table cell spans (UIA GridItem.RowSpan; rowspan), 1 when not a spanning cell.</summary>
    int RowSpan => 1;

    /// <summary>Columns a table cell spans (UIA GridItem.ColumnSpan; colspan), 1 when not a spanning cell.</summary>
    int ColumnSpan => 1;

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
        "MenuItem", "RadioButton", "Slider", "Spinner",
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
        var (allNodes, flatText) = BuildSubtree(root);
        return new VBufferDocument(flatText, allNodes[0], allNodes);
    }

    /// <summary>
    /// Builds the nodes for a subtree in pre-order, with Ids and text offsets starting at 0 and
    /// parent/child and document-order links set within the subtree. The root has no parent.
    /// Shared with <see cref="IncrementalUpdater"/>.
    /// </summary>
    /// <param name="inheritedLanguage">The language the subtree's root inherits (its old parent's, when splicing).</param>
    internal static (List<VBufferNode> Nodes, string FlatText) BuildSubtree(IVBufferElement root, string inheritedLanguage = "")
    {
        var allNodes = new List<VBufferNode>(64);
        var flatText = new StringBuilder(256);
        int nextId = 0;

        VBufferNode? prevInOrder = null;

        // Iterative DFS with explicit enter/exit frames (avoids recursion on deep pages).
        // Nodes are created on enter (pre-order); text is decided on exit, once we know
        // whether any descendant contributed text.
        var stack = new Stack<Frame>();
        stack.Push(new Frame(root, null, null, 0));

        while (stack.Count > 0)
        {
            var frame = stack.Pop();

            if (frame.Node is not null)
            {
                // Exit: children have been processed. An element that fails to report its text
                // just contributes none.
                try
                {
                    FinishNode(frame.Element, frame.Node, frame.TextStart, flatText);
                }
                catch (Exception) when (frame.Parent is not null)
                {
                }
                continue;
            }

            // An element that fails (a live provider whose element went away, or that errors)
            // is left out with its subtree rather than failing the whole page. Children are read
            // first: CreateNode links the node into its parent, so nothing is linked on failure.
            IReadOnlyList<IVBufferElement> children;
            VBufferNode node;
            try
            {
                children = frame.Element.GetChildren();
                node = CreateNode(frame.Element, frame.Parent, nextId, inheritedLanguage);
            }
            catch (Exception) when (frame.Parent is not null)
            {
                continue;
            }
            nextId++;
            allNodes.Add(node);

            // Link doubly-linked document-order list
            if (prevInOrder is not null)
            {
                prevInOrder.NextInOrder = node;
                node.PrevInOrder = prevInOrder;
            }
            prevInOrder = node;

            stack.Push(new Frame(frame.Element, frame.Parent, node, flatText.Length));

            // Push children in reverse order so we process them left-to-right
            for (int i = children.Count - 1; i >= 0; i--)
            {
                stack.Push(new Frame(children[i], node, null, 0));
            }
        }

        JoinInlineRuns(allNodes, flatText);
        return (allNodes, flatText.ToString());
    }

    /// <summary>
    /// Puts runs of inline siblings on one line: "Read the " + link "docs" + " first" becomes
    /// "Read the docs first" instead of three lines. Each run member except the last has its
    /// trailing '\n' replaced by a space — the same length, so no text offsets change.
    ///
    /// Inline means a leaf Text node or a link (UIA exposes no CSS display type, so this is a
    /// heuristic: block elements such as paragraphs and headings have children or a heading level).
    /// </summary>
    internal static void JoinInlineRuns(IReadOnlyList<VBufferNode> nodes, StringBuilder flatText)
    {
        // Exclusive end of each node's whole subtree text (nodes are in pre-order, Ids = indices)
        var subtreeEnd = new int[nodes.Count];
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            var node = nodes[i];
            int end = node.TextRange.End;
            foreach (var child in node.Children)
                end = Math.Max(end, subtreeEnd[child.Id]);
            subtreeEnd[i] = end;
        }

        foreach (var parent in nodes)
        {
            VBufferNode? previousInRun = null;
            foreach (var child in parent.Children)
            {
                bool hasText = subtreeEnd[child.Id] > child.TextRange.Start;
                if (!IsInline(child))
                {
                    previousInRun = null;
                    continue;
                }
                if (!hasText)
                    continue;

                // Join the previous inline sibling's line onto this one
                if (previousInRun is not null)
                {
                    int newline = subtreeEnd[previousInRun.Id] - 1;
                    if (newline >= 0 && flatText[newline] == '\n')
                        flatText[newline] = ' ';
                }
                previousInRun = child;
            }
        }
    }

    private static bool IsInline(VBufferNode node) =>
        node.HeadingLevel == 0 && !node.IsLandmark &&
        ((node.ControlType == "Text" && node.Children.Count == 0) || node.ControlType == "Hyperlink");

    private readonly record struct Frame(
        IVBufferElement Element,
        VBufferNode? Parent,
        VBufferNode? Node,
        int TextStart);

    private static VBufferNode CreateNode(IVBufferElement element, VBufferNode? parent, int id, string inheritedLanguage = "")
    {
        var ariaRole = element.AriaRole;
        var ariaProps = element.AriaProperties;

        // Parse heading level
        var headingLevel = element.HeadingLevel >= 1
            ? Math.Min(element.HeadingLevel, 6)
            : ParseHeadingLevel(ariaRole, ariaProps, element.ControlType);

        // Parse landmark type
        var landmarkType = ParseLandmarkType(ariaRole);

        // Parse link status
        var isLink = IsLinkElement(ariaRole, element.ControlType);

        // Parse ARIA properties
        var isVisited  = element.IsVisited || ParseAriaPropertyBool(ariaProps, "visited");
        var isRequired = element.IsRequired || ParseAriaPropertyBool(ariaProps, "required");
        var (isExpandable, isExpanded) = ControlState.Expansion(element.ExpandCollapseState, ariaProps, element.ControlType);

        // Determine focusability
        var isFocusable = element.IsFocusable ||
                          FocusableControlTypes.Contains(element.ControlType) ||
                          isLink;

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
            ToggleState = element.ToggleState,
            IsSelected = element.IsSelected,
            Value = element.Value ?? string.Empty,
            IsPassword = element.IsPassword,
            IsFocusable = isFocusable,
            // Inherited, as lang is in HTML
            Language = !string.IsNullOrEmpty(element.Language) ? element.Language : parent?.Language ?? inheritedLanguage,
            RowSpan = Math.Max(1, element.RowSpan),
            ColumnSpan = Math.Max(1, element.ColumnSpan),
            Parent = parent,
        };

        parent?.Children.Add(node);

        return node;
    }

    /// <summary>
    /// Decides the node's own text once its descendants are built.
    ///
    /// A node contributes its Name only when no descendant contributed text: in Chromium's tree
    /// a link or heading named "Foo" usually has a Text child also named "Foo", and emitting both
    /// would duplicate the text. Container control types never contribute text.
    ///
    /// Nodes that contribute nothing get an empty range positioned where their content starts,
    /// so TextRange.Start stays non-decreasing in document order.
    /// </summary>
    private static void FinishNode(IVBufferElement element, VBufferNode node, int textStart, StringBuilder flatText)
    {
        bool descendantsHaveText = flatText.Length > textStart;
        if (!descendantsHaveText)
            AppendNodeText(element, node, flatText);

        node.TextRange = descendantsHaveText
            ? (textStart, textStart)
            : (textStart, flatText.Length);
    }

    /// <summary>Control types that never contribute their own Name to the flat text.</summary>
    internal static bool IsContainerControlType(string controlType) =>
        controlType is "Document" or "Group" or "Pane" or "Window" or "Custom"
            or "ToolBar" or "Menu" or "MenuBar" or "StatusBar" or "TitleBar";

    private static void AppendNodeText(IVBufferElement element, VBufferNode node, StringBuilder flatText)
    {
        // Only leaf-like elements contribute text (not container elements like Document/Group/Pane)
        // We include text from: Text, Heading (via ariaRole), Link, Button, Edit, Image (alt text), etc.
        if (IsContainerControlType(element.ControlType)) return;

        // A form field reads as its label followed by its value ("Search hello"): Chromium's
        // inputs have no text child holding what was typed or selected
        var text = element.Name ?? string.Empty;
        var value = FormControls.BufferValue(node);
        if (value is not null)
            text = string.IsNullOrEmpty(text) ? value : $"{text} {value}";
        if (string.IsNullOrEmpty(text)) return;

        flatText.Append(text);
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
            "heading" => ParseHeadingLevelProperty(ariaProps),
            "h1" => 1,
            "h2" => 2,
            "h3" => 3,
            "h4" => 4,
            "h5" => 5,
            "h6" => 6,
            _ => 0
        };
    }

    /// <summary>
    /// Heading level from aria "level" for role=heading. ARIA's default is 2 when absent or invalid;
    /// levels above 6 are treated as 6 so they stay navigable.
    /// </summary>
    public static int ParseHeadingLevelProperty(string? ariaProps)
    {
        var level = ParseAriaPropertyInt(ariaProps ?? string.Empty, "level");
        return level >= 1 ? Math.Min(level, 6) : 2;
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
