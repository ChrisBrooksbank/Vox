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

    /// <summary>UIA ToggleState (0 off, 1 on, 2 indeterminate), or null when not a toggle.</summary>
    public int? ToggleState { get; init; }

    /// <summary>Whether a selectable element (radio button, tab, list item) is selected, or null.</summary>
    public bool? IsSelected { get; init; }

    /// <summary>UIA Value.Value: a text box's text, a combo box's selection, a slider's value.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>True for a password field (its value is never spoken).</summary>
    public bool IsPassword { get; init; }

    /// <summary>True if this element can receive keyboard focus.</summary>
    public bool IsFocusable { get; init; }

    /// <summary>Position among its siblings in a set (list item 3 of 10), or 0 when unknown.</summary>
    public int PositionInSet { get; init; }

    /// <summary>Size of the set it is in, or 0 when unknown.</summary>
    public int SizeOfSet { get; init; }

    /// <summary>Hierarchical level (tree item depth, 1-based), or 0 when unknown.</summary>
    public int Level { get; init; }

    /// <summary>Keyboard shortcut that runs it (a menu item's "Ctrl+O"), or empty.</summary>
    public string AcceleratorKey { get; init; } = string.Empty;

    /// <summary>Access key ("Alt+F", or the underlined letter of a menu item), or empty.</summary>
    public string AccessKey { get; init; } = string.Empty;

    /// <summary>Description (aria-description / aria-describedby), or empty.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Invalid entry (aria-invalid): "spelling", "grammar", "true" for any other, or empty when valid.
    /// </summary>
    public string Invalid { get; init; } = string.Empty;

    /// <summary>Runtime IDs of the elements holding an invalid entry's error message (aria-errormessage).</summary>
    public IReadOnlyList<int[]> ErrorMessageIds { get; init; } = [];

    /// <summary>
    /// The text of the error message elements, resolved from <see cref="ErrorMessageIds"/> by
    /// <see cref="VBufferDocument"/> when it is built; empty when there is none.
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>aria-current: "page", "step", "location", "date", "time", "true", or empty.</summary>
    public string Current { get; init; } = string.Empty;

    /// <summary>aria-sort: "ascending", "descending", "other", or empty.</summary>
    public string Sort { get; init; } = string.Empty;

    /// <summary>The author's name for the role (aria-roledescription), spoken instead of the role; or empty.</summary>
    public string RoleDescription { get; init; } = string.Empty;

    /// <summary>True when it has extended details elsewhere on the page (aria-details).</summary>
    public bool HasDetails { get; init; }

    /// <summary>
    /// The kind of annotation it is ("comment", "insertion", "deletion", "highlight",
    /// "suggestion"; see <see cref="Annotations"/>), or empty.
    /// </summary>
    public string Annotation { get; init; } = string.Empty;

    /// <summary>A modal dialog (aria-modal, or a &lt;dialog&gt; opened with showModal()): while it is open, browsing stays inside it.</summary>
    public bool IsModal { get; init; }

    /// <summary>
    /// A plain element (not a link or control) with a click handler, outermost only: said as "clickable".
    /// </summary>
    public bool IsClickable { get; init; }

    /// <summary>A rich text editor (contenteditable): edited in Focus mode like a text box.</summary>
    public bool IsRichEditable { get; init; }

    /// <summary>The text's language (BCP 47), its own or inherited from its ancestors; empty when unknown.</summary>
    public string Language { get; init; } = string.Empty;

    /// <summary>Rows a table cell spans (1 for an ordinary cell).</summary>
    public int RowSpan { get; init; } = 1;

    /// <summary>Columns a table cell spans (1 for an ordinary cell).</summary>
    public int ColumnSpan { get; init; } = 1;

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

    /// <summary>
    /// Copies this node's properties into a new node with the given Id and text range.
    /// Tree links (Parent, Children, PrevInOrder, NextInOrder) are not copied.
    /// </summary>
    public VBufferNode CloneDetached(int id, (int Start, int End) textRange) => new()
    {
        Id = id,
        UIARuntimeId = UIARuntimeId,
        Name = Name,
        ControlType = ControlType,
        AriaRole = AriaRole,
        HeadingLevel = HeadingLevel,
        LandmarkType = LandmarkType,
        IsLink = IsLink,
        IsVisited = IsVisited,
        IsRequired = IsRequired,
        IsExpandable = IsExpandable,
        IsExpanded = IsExpanded,
        ToggleState = ToggleState,
        IsSelected = IsSelected,
        Value = Value,
        IsPassword = IsPassword,
        IsFocusable = IsFocusable,
        AcceleratorKey = AcceleratorKey,
        Description = Description,
        Invalid = Invalid,
        ErrorMessageIds = ErrorMessageIds,
        Current = Current,
        Sort = Sort,
        RoleDescription = RoleDescription,
        HasDetails = HasDetails,
        Annotation = Annotation,
        IsModal = IsModal,
        IsClickable = IsClickable,
        IsRichEditable = IsRichEditable,
        Language = Language,
        RowSpan = RowSpan,
        ColumnSpan = ColumnSpan,
        TextRange = textRange,
    };

    public override string ToString() =>
        $"VBufferNode[{Id}] {ControlType} \"{Name}\"" +
        (IsHeading ? $" H{HeadingLevel}" : "") +
        (IsLink ? " link" : "") +
        (IsLandmark ? $" landmark:{LandmarkType}" : "");
}
