namespace Vox.Core.Navigation;

/// <summary>
/// Maps UIA control type names to what a screen reader user expects to hear.
/// Structural types that carry no meaning on their own (Chromium reports headings and
/// paragraphs as Text, generic containers as Group/Pane/Custom) are not spoken at all.
/// </summary>
public static class ControlTypeNames
{
    private static readonly Dictionary<string, string?> Spoken = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Text"] = null,
        ["Group"] = null,
        ["Pane"] = null,
        ["Custom"] = null,
        ["Unknown"] = null,
        ["Heading"] = null,
        ["Hyperlink"] = "link",
        ["ComboBox"] = "combo box",
        ["CheckBox"] = "check box",
        ["RadioButton"] = "radio button",
        ["ListItem"] = "list item",
        ["MenuItem"] = "menu item",
        ["MenuBar"] = "menu bar",
        ["TabItem"] = "tab",
        ["Tab"] = "tab list",
        ["TreeItem"] = "tree item",
        ["DataGrid"] = "grid",
        ["DataItem"] = "grid item",
        ["SplitButton"] = "split button",
        ["ProgressBar"] = "progress bar",
        ["ScrollBar"] = "scroll bar",
        ["StatusBar"] = "status bar",
        ["ToolBar"] = "tool bar",
        ["ToolTip"] = "tool tip",
        ["TitleBar"] = "title bar",
        ["HeaderItem"] = "column header",
        ["AppBar"] = "app bar",
        ["SemanticZoom"] = "semantic zoom",
    };

    /// <summary>
    /// The spoken name for <paramref name="controlType"/>, or null when it should not be spoken.
    /// Unmapped types are spoken in lower case (e.g. "Button" → "button").
    /// </summary>
    public static string? ToSpoken(string? controlType)
    {
        if (string.IsNullOrWhiteSpace(controlType))
            return null;
        return Spoken.TryGetValue(controlType, out var spoken) ? spoken : controlType.ToLowerInvariant();
    }
}
