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

    /// <summary>All form-field nodes (Edit, ComboBox, CheckBox, RadioButton, Spinner, ListItem with IsRequired, etc.), in document order.</summary>
    public IReadOnlyList<VBufferNode> FormFields { get; }

    /// <summary>All landmark nodes (nav, main, banner, contentinfo, search, complementary, form, region), in document order.</summary>
    public IReadOnlyList<VBufferNode> Landmarks { get; }

    /// <summary>All nodes where IsFocusable is true, in document order.</summary>
    public IReadOnlyList<VBufferNode> FocusableElements { get; }

    // Fast lookup tables
    private readonly Dictionary<string, VBufferNode> _byRuntimeId;
    private readonly VBufferNode[] _allNodesArray;

    private static readonly HashSet<string> FormFieldControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Edit", "ComboBox", "CheckBox", "RadioButton", "Spinner", "Slider", "List", "ListItem"
    };

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
        _byRuntimeId = new Dictionary<string, VBufferNode>(allNodes.Count);

        foreach (var node in allNodes)
        {
            if (node.IsHeading) headings.Add(node);
            if (node.IsLink) links.Add(node);
            if (IsFormField(node)) formFields.Add(node);
            if (node.IsLandmark) landmarks.Add(node);
            if (node.IsFocusable) focusable.Add(node);

            var key = RuntimeIdKey(node.UIARuntimeId);
            _byRuntimeId[key] = node;
        }

        Headings = headings;
        Links = links;
        FormFields = formFields;
        Landmarks = landmarks;
        FocusableElements = focusable;
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

    private static bool IsFormField(VBufferNode node) =>
        FormFieldControlTypes.Contains(node.ControlType) ||
        node.IsRequired ||
        node.IsExpandable;

    private static string RuntimeIdKey(int[] ids) =>
        string.Join(",", ids);
}
