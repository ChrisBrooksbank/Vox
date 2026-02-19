using Vox.Core.Buffer;

namespace Vox.Core.Navigation;

/// <summary>
/// Pure-logic view model for the Elements List dialog.
/// Contains no WinForms dependencies so it can be unit tested without a message pump.
///
/// Tabs (by index):
///   0 = Headings
///   1 = Links
///   2 = Landmarks
///   3 = Form Fields
/// </summary>
public sealed class ElementsListViewModel
{
    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    private readonly VBufferDocument _document;

    private int _selectedTabIndex;
    private string _filterText = string.Empty;

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

    /// <summary>Number of available tabs (always 4).</summary>
    public int TabCount => 4;

    /// <summary>Currently active tab index (0–3).</summary>
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
            if (GetDisplayText(node).Contains(filter, StringComparison.OrdinalIgnoreCase))
                result.Add(node);
        }
        return result;
    }

    /// <summary>
    /// Returns the display text for a given node as it would appear in the list.
    /// </summary>
    public static string GetDisplayText(VBufferNode node)
    {
        if (node.IsHeading)
            return $"H{node.HeadingLevel}: {node.Name}";

        if (node.IsLandmark)
        {
            return string.IsNullOrWhiteSpace(node.Name)
                ? node.LandmarkType
                : $"{node.LandmarkType}: {node.Name}";
        }

        if (!string.IsNullOrWhiteSpace(node.Name))
            return node.Name;

        return $"[{node.ControlType}]";
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
            3 => _document.FormFields,
            _ => Array.Empty<VBufferNode>(),
        };
}
