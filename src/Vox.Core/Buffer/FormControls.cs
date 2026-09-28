namespace Vox.Core.Buffer;

/// <summary>
/// Classifies form controls from their UIA control type and ARIA role.
///
/// Chromium maps plain HTML lists (&lt;ul&gt;/&lt;li&gt;) to the List/ListItem control types, so those
/// control types alone do not mean "form control"; real list boxes are recognised by ARIA role
/// (listbox/option), which Chromium also reports for &lt;select multiple&gt;.
/// </summary>
public static class FormControls
{
    private static readonly HashSet<string> FormControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Edit", "ComboBox", "CheckBox", "RadioButton", "Spinner", "Slider"
    };

    private static readonly HashSet<string> FormRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "textbox", "searchbox", "combobox", "listbox", "option", "checkbox", "radio", "switch",
        "spinbutton", "slider", "tree", "treeitem", "grid", "gridcell",
        "menu", "menuitem", "menuitemcheckbox", "menuitemradio"
    };

    // Controls whose keys (typing, arrows) must reach the control, i.e. that need Focus mode
    private static readonly HashSet<string> FocusModeControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Edit", "ComboBox", "Spinner", "Slider"
    };

    private static readonly HashSet<string> FocusModeRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "textbox", "searchbox", "combobox", "listbox", "option", "spinbutton", "slider",
        "tree", "treeitem", "grid", "gridcell"
    };

    /// <summary>True for form controls (F / Shift+F navigation, staying in Focus mode).</summary>
    // Controls whose value is part of what they are (the text in a text box, the chosen option)
    private static readonly HashSet<string> ValueControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Edit", "ComboBox", "Spinner", "Slider", "ProgressBar"
    };

    private static readonly HashSet<string> ValueRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "textbox", "searchbox", "combobox", "spinbutton", "slider", "progressbar"
    };

    /// <summary>True for controls whose value is spoken with them (text boxes, combo boxes, sliders).</summary>
    public static bool ShowsValue(string controlType, string? ariaRole) =>
        ValueControlTypes.Contains(controlType) || IsRole(ValueRoles, ariaRole);

    /// <summary>
    /// The value to speak for <paramref name="node"/>, or null: not a value control, a password
    /// field (never read), empty, or the same as the name.
    /// </summary>
    public static string? SpokenValue(VBufferNode node)
    {
        if (node.IsPassword || !ShowsValue(node.ControlType, node.AriaRole))
            return null;
        var value = node.Value?.Trim();
        if (string.IsNullOrEmpty(value) || string.Equals(value, node.Name?.Trim(), StringComparison.Ordinal))
            return null;
        return value;
    }

    public static bool IsFormField(string controlType, string? ariaRole) =>
        FormControlTypes.Contains(controlType) || IsRole(FormRoles, ariaRole);

    /// <summary>True for controls that are operated by typing or arrow keys.</summary>
    public static bool NeedsFocusMode(string controlType, string? ariaRole) =>
        FocusModeControlTypes.Contains(controlType) || IsRole(FocusModeRoles, ariaRole);

    private static bool IsRole(HashSet<string> roles, string? ariaRole) =>
        !string.IsNullOrWhiteSpace(ariaRole) && roles.Contains(ariaRole.Trim());
}
