using Vox.Core.Buffer;

namespace Vox.Core.Navigation;

/// <summary>
/// Pure-logic view model for the Elements List dialog.
/// Contains no WinForms dependencies so it can be unit tested without a message pump.
///
/// Tabs (by index, named by <see cref="TabNames"/>):
///   0 = Headings
///   1 = Links
///   2 = Landmarks
///   3 = Form Fields (narrowed to one kind by <see cref="FormFieldKind"/>)
///   4 = Buttons
///   5 = Tables
/// </summary>
public sealed class ElementsListViewModel
{
    /// <summary>The tabs' names, by index.</summary>
    public static IReadOnlyList<string> TabNames { get; } =
        ["Headings", "Links", "Landmarks", "Form Fields", "Buttons", "Tables"];

    /// <summary>Index of the Form Fields tab, the one <see cref="FormFieldKind"/> applies to.</summary>
    public const int FormFieldsTab = 3;

    /// <summary>The form field kinds' names, by <see cref="ElementsFormFieldKind"/> value.</summary>
    public static IReadOnlyList<string> FormFieldKindNames { get; } =
        ["All", "Edits", "Combo boxes", "Check boxes", "Radio buttons"];

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    private readonly VBufferDocument _document;

    private int _selectedTabIndex;
    private string _filterText = string.Empty;
    private ElementsFormFieldKind _formFieldKind;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public ElementsListViewModel(VBufferDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Number of available tabs.</summary>
    public int TabCount => TabNames.Count;

    /// <summary>Currently active tab index (0 to <see cref="TabCount"/> - 1).</summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (value < 0 || value >= TabCount)
                throw new ArgumentOutOfRangeException(nameof(value));

            if (_selectedTabIndex != value)
            {
                _selectedTabIndex = value;
                _filterText = string.Empty;   // reset filter on tab switch
            }
        }
    }

    /// <summary>Which kind of form field the Form Fields tab lists (all of them by default).</summary>
    public ElementsFormFieldKind FormFieldKind
    {
        get => _formFieldKind;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            _formFieldKind = value;
        }
    }

    /// <summary>Current filter text (set to narrow the list).</summary>
    public string FilterText
    {
        get => _filterText;
        set => _filterText = value ?? string.Empty;
    }

    /// <summary>
    /// Returns the filtered items for the currently selected tab,
    /// applying <see cref="FilterText"/> case-insensitively.
    /// </summary>
    public IReadOnlyList<VBufferNode> GetFilteredItems()
    {
        var source = GetItemsForTab(_selectedTabIndex);
        var filter = _filterText.Trim();

        if (filter.Length == 0)
            return source;

        var result = new List<VBufferNode>(source.Count);
        foreach (var node in source)
        {
            if (DisplayText(node).Contains(filter, StringComparison.OrdinalIgnoreCase))
                result.Add(node);
        }
        return result;
    }

    /// <summary>
    /// Index in <paramref name="items"/> to select first: the element containing
    /// <paramref name="current"/> (the virtual cursor's node), else the first one after it, else
    /// the last one — so the user starts where they are on the page. 0 when there is no current node.
    /// </summary>
    public static int InitialSelectionIndex(IReadOnlyList<VBufferNode> items, VBufferNode? current)
    {
        if (items.Count == 0 || current is null)
            return items.Count == 0 ? -1 : 0;

        for (var n = current; n is not null; n = n.Parent)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], n))
                    return i;
            }
        }
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Id > current.Id)
                return i;
        }
        return items.Count - 1;
    }

    /// <summary>
    /// How an item is shown: <see cref="GetDisplayText(VBufferNode, string?)"/> with this
    /// document's text, so unnamed links show their text (e.g. an image's alt text).
    /// </summary>
    public string DisplayText(VBufferNode node) => GetDisplayText(node, _document.FlatText);

    /// <summary>
    /// Returns the display text for a given node as it would appear in the list.
    /// </summary>
    public static string GetDisplayText(VBufferNode node, string? flatText = null)
    {
        // Unnamed elements (a link around an image, a heading made of child elements) show the
        // text they contribute to the page instead
        var name = !string.IsNullOrWhiteSpace(node.Name) ? node.Name : SubtreeText(node, flatText);

        if (node.IsHeading)
            return $"H{node.HeadingLevel}: {name}";

        if (node.IsLandmark)
        {
            return string.IsNullOrWhiteSpace(node.Name)
                ? node.LandmarkType
                : $"{node.LandmarkType}: {node.Name}";
        }

        // Form fields: what kind of field, and what is in it ("Search, edit, hello")
        if (FormControls.IsFormField(node.ControlType, node.AriaRole))
        {
            // An unnamed field's own buffer text is just its value, which is added below:
            // don't show it twice ("hello, edit, hello")
            if (string.IsNullOrWhiteSpace(node.Name) && node.HasText)
                name = string.Empty;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(name)) parts.Add(name);
            var role = ControlTypeNames.ToSpoken(node.ControlType);
            if (role is not null) parts.Add(role);
            var value = FormControls.SpokenValue(node);
            if (value is not null) parts.Add(value);
            if (parts.Count > 0)
                return string.Join(", ", parts);
        }

        if (!string.IsNullOrWhiteSpace(name))
            return name;

        return $"[{node.ControlType}]";
    }

    private const int MaxSubtreeTextLength = 80;

    /// <summary>The text <paramref name="node"/> and its descendants contribute to the flat text, on one line.</summary>
    private static string SubtreeText(VBufferNode node, string? flatText)
    {
        if (string.IsNullOrEmpty(flatText))
            return string.Empty;

        int end = node.TextRange.End;
        var stack = new Stack<VBufferNode>(node.Children);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            end = Math.Max(end, n.TextRange.End);
            foreach (var child in n.Children)
                stack.Push(child);
        }

        int start = Math.Clamp(node.TextRange.Start, 0, flatText.Length);
        end = Math.Clamp(end, start, flatText.Length);
        var text = string.Join(" ", flatText[start..end].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return text.Length > MaxSubtreeTextLength ? text[..MaxSubtreeTextLength].TrimEnd() + "…" : text;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private IReadOnlyList<VBufferNode> GetItemsForTab(int tabIndex) =>
        tabIndex switch
        {
            0 => _document.Headings,
            1 => _document.Links,
            2 => _document.Landmarks,
            FormFieldsTab => _formFieldKind switch
            {
                ElementsFormFieldKind.Edits => _document.Edits,
                ElementsFormFieldKind.ComboBoxes => _document.ComboBoxes,
                ElementsFormFieldKind.CheckBoxes => _document.CheckBoxes,
                ElementsFormFieldKind.RadioButtons => _document.RadioButtons,
                _ => _document.FormFields,
            },
            4 => _document.Buttons,
            5 => _document.Tables,
            _ => Array.Empty<VBufferNode>(),
        };
}

/// <summary>The kinds of form field the Elements List's Form Fields tab can be narrowed to.</summary>
public enum ElementsFormFieldKind
{
    All,
    Edits,
    ComboBoxes,
    CheckBoxes,
    RadioButtons,
}
