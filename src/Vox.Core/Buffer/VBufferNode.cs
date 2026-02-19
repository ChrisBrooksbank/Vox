namespace Vox.Core.Buffer;

/// <summary>
/// A single node in the virtual buffer document tree.
/// Represents one UIA element with all properties needed for screen reader navigation.
/// </summary>
public sealed class VBufferNode
{
    /// <summary>Sequential document-order ID assigned by VBufferBuilder.</summary>
    public int Id { get; init; }

    /// <summary>UIA runtime ID for matching back to live UIA elements.</summary>
    public int[] UIARuntimeId { get; init; } = [];

    /// <summary>Accessible name of the element.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>UIA ControlType name (e.g. "Heading", "Link", "Edit", "Document").</summary>
    public string ControlType { get; init; } = string.Empty;

    /// <summary>AriaRole string from UIA (e.g. "heading", "navigation", "link").</summary>
    public string AriaRole { get; init; } = string.Empty;

    /// <summary>Heading level 1-6, or 0 if not a heading.</summary>
    public int HeadingLevel { get; init; }

    /// <summary>Landmark type if element is a landmark (e.g. "main", "nav", "banner"), or empty string.</summary>
    public string LandmarkType { get; init; } = string.Empty;

    /// <summary>True if this element is a link.</summary>
    public bool IsLink { get; init; }

    /// <summary>True if this link has been visited.</summary>
    public bool IsVisited { get; init; }

    /// <summary>True if this form field is required (aria-required).</summary>
    public bool IsRequired { get; init; }

    /// <summary>True if this element is expandable (e.g. combobox, tree item).</summary>
    public bool IsExpandable { get; init; }

    /// <summary>True if this element is currently expanded.</summary>
    public bool IsExpanded { get; init; }

    /// <summary>True if this element can receive keyboard focus.</summary>
    public bool IsFocusable { get; init; }

    /// <summary>
    /// Text content contributed by this node to the flat document text.
    /// Represents the character range (start, end) in VBufferDocument.FlatText.
    /// Start is inclusive, End is exclusive.
    /// </summary>
    public (int Start, int End) TextRange { get; set; }

    // Tree structure — set during build, linked list for O(1) traversal
    public VBufferNode? Parent { get; set; }
    public List<VBufferNode> Children { get; } = new();

    /// <summary>Previous node in document order (pre-order traversal).</summary>
    public VBufferNode? PrevInOrder { get; set; }

    /// <summary>Next node in document order (pre-order traversal).</summary>
    public VBufferNode? NextInOrder { get; set; }

    /// <summary>
    /// Returns true if this node represents a heading at any level (1-6).
    /// </summary>
    public bool IsHeading => HeadingLevel is >= 1 and <= 6;

    /// <summary>
    /// Returns true if this node represents a landmark region.
    /// </summary>
    public bool IsLandmark => !string.IsNullOrEmpty(LandmarkType);

    /// <summary>
    /// Returns true if this node has any text content.
    /// </summary>
    public bool HasText => TextRange.End > TextRange.Start;

    public override string ToString() =>
        $"VBufferNode[{Id}] {ControlType} \"{Name}\"" +
        (IsHeading ? $" H{HeadingLevel}" : "") +
        (IsLink ? " link" : "") +
        (IsLandmark ? $" landmark:{LandmarkType}" : "");
}
