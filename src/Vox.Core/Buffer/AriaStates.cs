namespace Vox.Core.Buffer;

/// <summary>
/// ARIA states that UIA carries in AriaProperties or in its own properties, as Core-AAM maps
/// them: <c>aria-invalid</c> (IsDataValidForForm, or invalid=spelling/grammar), <c>aria-current</c>
/// (current=page), <c>aria-sort</c> (sort=ascending) and <c>aria-pressed</c> (the Toggle pattern of
/// a button, or pressed=true), and the words a user hears for each.
/// </summary>
public static class AriaStates
{
    /// <summary>
    /// A property's value in an AriaProperties string ("key=value;key2=value2", or with ':' and
    /// ','), trimmed and lower case; null when the key is absent.
    /// </summary>
    public static string? Value(string? ariaProps, string key)
    {
        if (string.IsNullOrEmpty(ariaProps)) return null;
        foreach (var segment in ariaProps.Split(';', ','))
        {
            var sep = segment.IndexOf('=');
            if (sep < 0) sep = segment.IndexOf(':');
            if (sep > 0 && string.Equals(segment[..sep].Trim(), key, StringComparison.OrdinalIgnoreCase))
                return segment[(sep + 1)..].Trim().ToLowerInvariant();
        }
        return null;
    }

    /// <summary>
    /// The kind of invalid entry: "spelling" or "grammar" (aria-invalid's own values), "true" for
    /// any other invalid entry, or empty when valid. <paramref name="isDataValidForForm"/> is UIA
    /// IsDataValidForForm (null when not reported).
    /// </summary>
    public static string Invalid(bool? isDataValidForForm, string? ariaProps)
    {
        var aria = Value(ariaProps, "invalid");
        if (aria is "spelling" or "grammar")
            return aria;
        if (isDataValidForForm == false || (aria is not null && aria is not ("false" or "" or "0" or "no")))
            return "true";
        return string.Empty;
    }

    /// <summary>
    /// aria-current: "page", "step", "location", "date", "time", or "true" for any other value;
    /// empty when not current.
    /// </summary>
    public static string Current(string? ariaProps) => Value(ariaProps, "current") switch
    {
        null or "" or "false" or "0" or "no" => string.Empty,
        var kind when kind is "page" or "step" or "location" or "date" or "time" => kind,
        _ => "true",
    };

    /// <summary>aria-sort: "ascending", "descending" or "other"; empty when not sorted.</summary>
    public static string Sort(string? ariaProps) => Value(ariaProps, "sort") switch
    {
        "ascending" => "ascending",
        "descending" => "descending",
        "other" => "other",
        _ => string.Empty,
    };

    /// <summary>
    /// aria-pressed as a UIA ToggleState (pressed=true is on, false off, mixed indeterminate);
    /// null when not set.
    /// </summary>
    public static int? Pressed(string? ariaProps) => Value(ariaProps, "pressed") switch
    {
        "true" => ControlState.ToggleOn,
        "false" => ControlState.ToggleOff,
        "mixed" => ControlState.ToggleIndeterminate,
        _ => null,
    };

    /// <summary>
    /// True for a toggle button (aria-pressed): a button whose state is pressed or not pressed
    /// rather than checked.
    /// </summary>
    public static bool IsToggleButton(string? controlType, string? ariaRole, int? toggleState) =>
        toggleState is not null && IsButton(controlType, ariaRole);

    /// <summary>A button or split button, by control type or ARIA role (the only roles aria-pressed applies to).</summary>
    public static bool IsButton(string? controlType, string? ariaRole) =>
        controlType is "Button" or "SplitButton"
        || string.Equals(ariaRole?.Trim(), "button", StringComparison.OrdinalIgnoreCase);

    /// <summary>Spoken text for an invalid entry kind ("invalid entry"), or null when valid.</summary>
    public static string? InvalidText(string? invalid) => invalid switch
    {
        null or "" => null,
        "spelling" => "spelling error",
        "grammar" => "grammar error",
        _ => "invalid entry",
    };

    /// <summary>Spoken text for aria-current ("current page"), or null when not current.</summary>
    public static string? CurrentText(string? current) => current switch
    {
        null or "" => null,
        "true" => "current",
        _ => $"current {current}",
    };

    /// <summary>Spoken text for aria-sort ("sorted ascending"), or null when not sorted.</summary>
    public static string? SortText(string? sort) => sort switch
    {
        "ascending" => "sorted ascending",
        "descending" => "sorted descending",
        "other" => "sorted",
        _ => null,
    };

    /// <summary>Spoken text for a toggle button's ToggleState ("pressed"), or null when not a toggle.</summary>
    public static string? PressedText(int? toggleState) => toggleState switch
    {
        ControlState.ToggleOff => "not pressed",
        ControlState.ToggleOn => "pressed",
        ControlState.ToggleIndeterminate => "half pressed",
        _ => null,
    };
}
