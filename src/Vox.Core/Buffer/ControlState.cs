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

        var expanded = VBufferBuilder.ParseAriaPropertyBool(ariaProps ?? string.Empty, "expanded");
        var expandable = expanded
            || VBufferBuilder.ParseAriaPropertyBool(ariaProps ?? string.Empty, "haspopup")
            || AriaPropertyPresent(ariaProps, "expanded")
            || string.Equals(controlType, "ComboBox", StringComparison.OrdinalIgnoreCase);
        return (expandable, expanded);
    }

    /// <summary>True when <paramref name="key"/> appears in the ARIA properties at all (e.g. expanded=false).</summary>
    private static bool AriaPropertyPresent(string? ariaProps, string key)
    {
        if (string.IsNullOrEmpty(ariaProps)) return false;
        foreach (var segment in ariaProps.Split(';', ','))
        {
            var sep = segment.IndexOf('=');
            if (sep < 0) sep = segment.IndexOf(':');
            if (sep > 0 && string.Equals(segment[..sep].Trim(), key, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
