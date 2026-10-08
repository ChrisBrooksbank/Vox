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
    // (menus and tab lists are operated with the arrow keys, as in NVDA)
    private static readonly HashSet<string> FocusModeControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Edit", "ComboBox", "Spinner", "Slider", "Menu", "MenuBar", "MenuItem", "TabItem"
    };

    private static readonly HashSet<string> FocusModeRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "textbox", "searchbox", "combobox", "listbox", "option", "spinbutton", "slider",
        "tree", "treeitem", "grid", "gridcell",
        "menu", "menubar", "menuitem", "menuitemcheckbox", "menuitemradio", "tablist", "tab"
    };

    // Controls whose value is part of what they are (the text in a text box, the chosen option)
    private static readonly HashSet<string> ValueControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Edit", "ComboBox", "Spinner", "Slider", "ProgressBar"
    };

    private static readonly HashSet<string> ValueRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "textbox", "searchbox", "combobox", "spinbutton", "slider", "progressbar"
    };

    /// <summary>A button (including toggle buttons), by control type or ARIA role.</summary>
    public static bool IsButton(VBufferNode node) =>
        Is(node, "Button", "button");

    /// <summary>A text field (not a combo box's), by control type or ARIA role.</summary>
    public static bool IsEdit(VBufferNode node) =>
        Is(node, "Edit", "textbox") || string.Equals(node.AriaRole, "searchbox", StringComparison.OrdinalIgnoreCase);

    /// <summary>A combo box (a drop-down list), by control type or ARIA role.</summary>
    public static bool IsComboBox(VBufferNode node) =>
        Is(node, "ComboBox", "combobox");

    /// <summary>A check box or switch, by control type or ARIA role.</summary>
    public static bool IsCheckBox(VBufferNode node) =>
        Is(node, "CheckBox", "checkbox") || string.Equals(node.AriaRole, "switch", StringComparison.OrdinalIgnoreCase);

    /// <summary>A radio button, by control type or ARIA role.</summary>
    public static bool IsRadioButton(VBufferNode node) =>
        Is(node, "RadioButton", "radio");

    private static bool Is(VBufferNode node, string controlType, string ariaRole) =>
        string.Equals(node.ControlType, controlType, StringComparison.OrdinalIgnoreCase)
        || string.Equals(node.AriaRole, ariaRole, StringComparison.OrdinalIgnoreCase);

    /// <summary>True for controls whose value is spoken with them (text boxes, combo boxes, sliders).</summary>
    public static bool ShowsValue(string controlType, string? ariaRole) =>
        ValueControlTypes.Contains(controlType) || IsRole(ValueRoles, ariaRole);

    /// <summary>
    /// The value to speak for <paramref name="node"/>, or null: not a value control, a password
    /// field (never read), empty, or the same as the name.
    /// </summary>
    /// <remarks>
    /// Only the first line is spoken, cut at a word boundary within <paramref name="maxLength"/>
    /// and marked with "…": a large text box must not be read out in full on focus.
    /// </remarks>
    public static string? SpokenValue(VBufferNode node, int maxLength = DefaultSpokenValueLength)
    {
        var value = ValueToShow(node);
        if (value is null)
            return null;

        int newline = value.IndexOfAny(['\r', '\n']);
        bool truncated = newline >= 0;
        if (truncated)
            value = value[..newline].TrimEnd();

        if (maxLength > 0 && value.Length > maxLength)
        {
            int cut = value.LastIndexOf(' ', maxLength);
            value = value[..(cut > 0 ? cut : maxLength)].TrimEnd();
            truncated = true;
        }
        return truncated ? value + "…" : value;
    }

    /// <summary>
    /// The value to put in the virtual buffer for <paramref name="node"/> (same rules as
    /// <see cref="SpokenValue"/>, capped at <see cref="MaxBufferValueLength"/> characters).
    /// </summary>
    public static string? BufferValue(VBufferNode node)
    {
        var value = ValueToShow(node)?.Replace("\r\n", "\n").Replace('\r', '\n');
        if (value is null || value.Length <= MaxBufferValueLength)
            return value;
        return value[..MaxBufferValueLength].TrimEnd() + "…";
    }

    public const int DefaultSpokenValueLength = 100;
    public const int MaxBufferValueLength = 1000;

    private static string? ValueToShow(VBufferNode node)
    {
        if (node.IsPassword || !ShowsValue(node.ControlType, node.AriaRole))
            return null;
        var value = node.Value?.Trim();
        if (string.IsNullOrEmpty(value) || string.Equals(value, node.Name?.Trim(), StringComparison.Ordinal))
            return null;
        return value;
    }

    /// <summary>True for form controls (F / Shift+F navigation).</summary>
    public static bool IsFormField(string controlType, string? ariaRole) =>
        FormControlTypes.Contains(controlType) || IsRole(FormRoles, ariaRole);

    /// <summary>True for controls that are operated by typing or arrow keys.</summary>
    public static bool NeedsFocusMode(string controlType, string? ariaRole) =>
        FocusModeControlTypes.Contains(controlType) || IsRole(FocusModeRoles, ariaRole);

    private static bool IsRole(HashSet<string> roles, string? ariaRole) =>
        !string.IsNullOrWhiteSpace(ariaRole) && roles.Contains(ariaRole.Trim());
}
