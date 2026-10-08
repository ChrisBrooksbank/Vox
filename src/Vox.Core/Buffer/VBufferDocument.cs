using System.Runtime.InteropServices;

namespace Vox.Core.Buffer;

/// <summary>
/// An immutable snapshot of a web page as a virtual buffer.
/// Contains the flat text representation, the node tree, pre-built indices,
/// and lookup methods needed for navigation.
/// </summary>
public sealed class VBufferDocument
{
    /// <summary>All text on the page joined with newline separators.</summary>
    public string FlatText { get; }

    /// <summary>Root node of the document tree (the Document element itself).</summary>
    public VBufferNode Root { get; }

    /// <summary>All nodes in document (pre-order) traversal order.</summary>
    public IReadOnlyList<VBufferNode> AllNodes { get; }

    // Pre-built index collections
    /// <summary>All nodes with HeadingLevel 1–6, in document order.</summary>
    public IReadOnlyList<VBufferNode> Headings { get; }

    /// <summary>All nodes where IsLink is true, in document order.</summary>
    public IReadOnlyList<VBufferNode> Links { get; }

    /// <summary>All form-field nodes (see <see cref="FormControls.IsFormField"/>, plus required or expandable nodes), in document order.</summary>
    public IReadOnlyList<VBufferNode> FormFields { get; }

    /// <summary>All landmark nodes (nav, main, banner, contentinfo, search, complementary, form, region), in document order.</summary>
    public IReadOnlyList<VBufferNode> Landmarks { get; }

    /// <summary>All nodes where IsFocusable is true, in document order.</summary>
    public IReadOnlyList<VBufferNode> FocusableElements { get; }

    /// <summary>Tables and grids, in document order.</summary>
    public IReadOnlyList<VBufferNode> Tables { get; }

    /// <summary>Buttons, in document order.</summary>
    public IReadOnlyList<VBufferNode> Buttons { get; }

    /// <summary>Text fields, in document order.</summary>
    public IReadOnlyList<VBufferNode> Edits { get; }

    /// <summary>Combo boxes, in document order.</summary>
    public IReadOnlyList<VBufferNode> ComboBoxes { get; }

    /// <summary>Check boxes and switches, in document order.</summary>
    public IReadOnlyList<VBufferNode> CheckBoxes { get; }

    /// <summary>Radio buttons, in document order.</summary>
    public IReadOnlyList<VBufferNode> RadioButtons { get; }

    /// <summary>Lists, in document order.</summary>
    public IReadOnlyList<VBufferNode> Lists { get; }

    /// <summary>List items, in document order.</summary>
    public IReadOnlyList<VBufferNode> ListItems { get; }

    /// <summary>Images and other graphics, in document order.</summary>
    public IReadOnlyList<VBufferNode> Graphics { get; }

    /// <summary>Block quotes, in document order.</summary>
    public IReadOnlyList<VBufferNode> BlockQuotes { get; }

    /// <summary>Frames (iframes and nested documents), in document order.</summary>
    public IReadOnlyList<VBufferNode> Frames { get; }

    /// <summary>Separators, in document order.</summary>
    public IReadOnlyList<VBufferNode> Separators { get; }

    /// <summary>Embedded objects (plug-ins, applications, audio and video), in document order.</summary>
    public IReadOnlyList<VBufferNode> EmbeddedObjects { get; }

    // Fast lookup tables
    private readonly Dictionary<string, VBufferNode> _byRuntimeId;
    private readonly VBufferNode[] _allNodesArray;

    public VBufferDocument(
        string flatText,
        VBufferNode root,
        IReadOnlyList<VBufferNode> allNodes)
    {
        FlatText = flatText;
        Root = root;
        AllNodes = allNodes;
        _allNodesArray = allNodes is VBufferNode[] arr ? arr : allNodes.ToArray();

        // Build indices
        var headings = new List<VBufferNode>();
        var links = new List<VBufferNode>();
        var formFields = new List<VBufferNode>();
        var landmarks = new List<VBufferNode>();
        var focusable = new List<VBufferNode>();
        var tables = new List<VBufferNode>();
        var buttons = new List<VBufferNode>();
        var edits = new List<VBufferNode>();
        var comboBoxes = new List<VBufferNode>();
        var checkBoxes = new List<VBufferNode>();
        var radioButtons = new List<VBufferNode>();
        var lists = new List<VBufferNode>();
        var listItems = new List<VBufferNode>();
        var graphics = new List<VBufferNode>();
        var blockQuotes = new List<VBufferNode>();
        var frames = new List<VBufferNode>();
        var separators = new List<VBufferNode>();
        var embeddedObjects = new List<VBufferNode>();
        _byRuntimeId = new Dictionary<string, VBufferNode>(allNodes.Count);

        foreach (var node in allNodes)
        {
            if (node.IsHeading) headings.Add(node);
            if (node.IsLink) links.Add(node);
            if (IsFormField(node)) formFields.Add(node);
            if (node.IsLandmark) landmarks.Add(node);
            if (node.IsFocusable) focusable.Add(node);
            if (IsTable(node)) tables.Add(node);
            if (FormControls.IsButton(node)) buttons.Add(node);
            if (FormControls.IsEdit(node)) edits.Add(node);
            if (FormControls.IsComboBox(node)) comboBoxes.Add(node);
            if (FormControls.IsCheckBox(node)) checkBoxes.Add(node);
            if (FormControls.IsRadioButton(node)) radioButtons.Add(node);
            if (PageElements.IsList(node)) lists.Add(node);
            if (PageElements.IsListItem(node)) listItems.Add(node);
            if (PageElements.IsGraphic(node)) graphics.Add(node);
            if (PageElements.IsBlockQuote(node)) blockQuotes.Add(node);
            if (PageElements.IsFrame(node)) frames.Add(node);
            if (PageElements.IsSeparator(node)) separators.Add(node);
            if (PageElements.IsEmbeddedObject(node)) embeddedObjects.Add(node);

            var key = RuntimeIdKey(node.UIARuntimeId);
            _byRuntimeId[key] = node;
        }

        Headings = headings;
        Links = links;
        FormFields = formFields;
        Landmarks = landmarks;
        FocusableElements = focusable;
        Tables = tables;
        Buttons = buttons;
        Edits = edits;
        ComboBoxes = comboBoxes;
        CheckBoxes = checkBoxes;
        RadioButtons = radioButtons;
        Lists = lists;
        ListItems = listItems;
        Graphics = graphics;
        BlockQuotes = blockQuotes;
        Frames = frames;
        Separators = separators;
        EmbeddedObjects = embeddedObjects;
    }

    /// <summary>
    /// Finds a node by its UIA runtime ID array.
    /// Returns null if not found.
    /// </summary>
    public VBufferNode? FindByRuntimeId(int[] runtimeId)
    {
        var key = RuntimeIdKey(runtimeId);
        return _byRuntimeId.TryGetValue(key, out var node) ? node : null;
    }

    /// <summary>
    /// Finds the node whose TextRange contains the given character offset.
    /// Returns null if the offset is out of range or no node covers it.
    /// Uses binary search on document-ordered nodes for efficiency.
    /// </summary>
    public VBufferNode? FindNodeAtOffset(int offset)
    {
        if (offset < 0 || offset >= FlatText.Length)
            return null;

        // Binary search: find the last node whose TextRange.Start <= offset
        int lo = 0, hi = _allNodesArray.Length - 1, result = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            var node = _allNodesArray[mid];
            if (node.TextRange.Start <= offset)
            {
                if (node.TextRange.End > offset)
                    return node; // exact hit
                // node ends before offset; keep searching right
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        // Fall back to linear scan from the last candidate (handles overlapping ranges)
        if (result >= 0)
        {
            for (int i = result; i >= 0; i--)
            {
                var node = _allNodesArray[i];
                if (node.TextRange.Start <= offset && node.TextRange.End > offset)
                    return node;
            }
        }

        return null;
    }

    /// <summary>True for tables and grids (UIA Table/DataGrid, or ARIA table/grid/treegrid).</summary>
    public static bool IsTable(VBufferNode node) =>
        node.ControlType is "Table" or "DataGrid"
        || node.AriaRole?.Trim().ToLowerInvariant() is "table" or "grid" or "treegrid";

    private static bool IsFormField(VBufferNode node) =>
        FormControls.IsFormField(node.ControlType, node.AriaRole) ||
        node.IsRequired ||
        node.IsExpandable;

    private static string RuntimeIdKey(int[] ids) =>
        string.Join(",", ids);
}
