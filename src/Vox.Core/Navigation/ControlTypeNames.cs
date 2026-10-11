using System.Globalization;
using Vox.Core.Localization;

namespace Vox.Core.Navigation;

/// <summary>
/// Maps UIA control type names to what a screen reader user expects to hear, in the language in
/// use (the <c>ControlType.&lt;type&gt;</c> strings).
/// Structural types that carry no meaning on their own (Chromium reports headings and
/// paragraphs as Text, generic containers as Group/Pane/Custom) are not spoken at all.
/// </summary>
public static class ControlTypeNames
{
    private static readonly HashSet<string> Silent = new(StringComparer.OrdinalIgnoreCase)
    {
        "Text", "Group", "Pane", "Custom", "Unknown", "Heading",
    };

    // The UIA control types with a string, by any casing → the string's name
    private static readonly Dictionary<string, string> Known = new[]
    {
        "AppBar", "Button", "Calendar", "CheckBox", "ComboBox", "DataGrid", "DataItem", "Document",
        "Edit", "Header", "HeaderItem", "Hyperlink", "Image", "List", "ListItem", "Menu", "MenuBar",
        "MenuItem", "ProgressBar", "RadioButton", "ScrollBar", "SemanticZoom", "Separator", "Slider",
        "Spinner", "SplitButton", "StatusBar", "Tab", "TabItem", "Table", "Thumb", "TitleBar",
        "ToolBar", "ToolTip", "Tree", "TreeItem", "Window",
    }.ToDictionary(t => t, t => "ControlType." + t, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The spoken name for <paramref name="controlType"/>, or null when it should not be spoken.
    /// Unmapped types are spoken in lower case (e.g. "Widget" → "widget").
    /// </summary>
    public static string? ToSpoken(string? controlType)
    {
        if (string.IsNullOrWhiteSpace(controlType) || Silent.Contains(controlType))
            return null;
        return Known.TryGetValue(controlType, out var key)
            ? VoxStrings.GetOrDefault(key, controlType.ToLowerInvariant())
            : controlType.ToLowerInvariant();
    }

    /// <summary>
    /// The spoken name in a given language (<see cref="CultureInfo.InvariantCulture"/>: English),
    /// whatever language Vox is using; null when it is not spoken.
    /// </summary>
    public static string? ToSpoken(string? controlType, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(controlType) || Silent.Contains(controlType))
            return null;
        return Known.TryGetValue(controlType, out var key)
            ? VoxStrings.Resources.GetString(key, culture) ?? controlType.ToLowerInvariant()
            : controlType.ToLowerInvariant();
    }
}
