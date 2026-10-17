namespace Vox.Core.Buffer;

/// <summary>
/// Combines UIA control patterns and ARIA properties into the states a user hears.
/// UIA values win when present: Chromium reports expand/collapse and checked state through the
/// ExpandCollapse and Toggle patterns rather than (reliably) through AriaProperties.
/// </summary>
public static class ControlState
{
    /// <summary>UIA ExpandCollapseState values.</summary>
    public const int Collapsed = 0, Expanded = 1, PartiallyExpanded = 2, LeafNode = 3;

    /// <summary>UIA ToggleState values.</summary>
    public const int ToggleOff = 0, ToggleOn = 1, ToggleIndeterminate = 2;

    /// <summary>
    /// Whether the element can expand and is expanded. <paramref name="uiaState"/> is the
    /// ExpandCollapseState (null when the pattern isn't supported); LeafNode means "not expandable".
    /// Without it, ARIA's expanded/haspopup are used.
    /// </summary>
    public static (bool IsExpandable, bool IsExpanded) Expansion(int? uiaState, string? ariaProps, string controlType)
    {
        if (uiaState is { } state)
        {
            return state switch
            {
                Collapsed => (true, false),
                Expanded or PartiallyExpanded => (true, true),
                _ => (false, false),
            };
        }

        // aria-expanded=false alone says nothing: Chromium lists expanded=false on every element, and
        // a control that really can expand offers UIA's ExpandCollapse pattern
        var expanded = VBufferBuilder.ParseAriaPropertyBool(ariaProps ?? string.Empty, "expanded");
        var expandable = expanded
            || HasPopup(ariaProps)
            || string.Equals(controlType, "ComboBox", StringComparison.OrdinalIgnoreCase);
        return (expandable, expanded);
    }

    /// <summary>
    /// aria-haspopup set to anything but false: since ARIA 1.1 it names the popup's kind
    /// (menu, listbox, tree, grid, dialog), and "true" means menu.
    /// </summary>
    private static bool HasPopup(string? ariaProps)
    {
        var reader = new AriaPropertyReader(ariaProps);
        while (reader.MoveNext())
        {
            if (!reader.IsKey("haspopup"))
                continue;
            var value = reader.Value.ToString().ToLowerInvariant();
            return value is "true" or "1" or "yes" or "menu" or "listbox" or "tree" or "grid" or "dialog";
        }
        return false;
    }
}
