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
    public static bool IsFormField(string controlType, string? ariaRole) =>
        FormControlTypes.Contains(controlType) || IsRole(FormRoles, ariaRole);

    /// <summary>True for controls that are operated by typing or arrow keys.</summary>
    public static bool NeedsFocusMode(string controlType, string? ariaRole) =>
        FocusModeControlTypes.Contains(controlType) || IsRole(FocusModeRoles, ariaRole);

    private static bool IsRole(HashSet<string> roles, string? ariaRole) =>
        !string.IsNullOrWhiteSpace(ariaRole) && roles.Contains(ariaRole.Trim());
}
