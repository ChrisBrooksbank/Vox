using Vox.Core.Buffer;
using Vox.Core.Pipeline;

namespace Vox.Core.Braille;

/// <summary>
/// What braille shows of an object (the focus, one of its ancestors, an element of a page): the
/// same properties speech uses, from a focus event or a virtual buffer node.
/// </summary>
public sealed record BrailleObject
{
    public string Name { get; init; } = string.Empty;
    /// <summary>The UIA control type name ("Button", "Hyperlink").</summary>
    public string ControlType { get; init; } = string.Empty;
    public string AriaRole { get; init; } = string.Empty;
    public string LandmarkType { get; init; } = string.Empty;
    public string RoleDescription { get; init; } = string.Empty;
    public int HeadingLevel { get; init; }
    public bool IsLink { get; init; }
    public bool IsVisited { get; init; }
    public bool IsRequired { get; init; }
    public bool IsExpandable { get; init; }
    public bool IsExpanded { get; init; }
    /// <summary>UIA ToggleState (0 off, 1 on, 2 indeterminate), or null when not a toggle.</summary>
    public int? ToggleState { get; init; }
    public bool? IsSelected { get; init; }
    public string Value { get; init; } = string.Empty;
    public bool IsPassword { get; init; }
    public bool IsReadOnly { get; init; }
    public int PositionInSet { get; init; }
    public int SizeOfSet { get; init; }
    public int Level { get; init; }
    public string Invalid { get; init; } = string.Empty;
    public string Current { get; init; } = string.Empty;
    /// <summary>The element's runtime id, to tell objects apart (focus context) and find them again.</summary>
    public int[]? RuntimeId { get; init; }

    public static BrailleObject From(FocusChangedEvent focus) => new()
    {
        Name = focus.ElementName,
        ControlType = focus.ControlType,
        AriaRole = focus.AriaRole ?? string.Empty,
        LandmarkType = focus.LandmarkType ?? string.Empty,
        RoleDescription = focus.RoleDescription ?? string.Empty,
        HeadingLevel = focus.HeadingLevel,
        IsLink = focus.IsLink,
        IsVisited = focus.IsVisited,
        IsRequired = focus.IsRequired,
        IsExpandable = focus.IsExpandable,
        IsExpanded = focus.IsExpanded,
        ToggleState = focus.ToggleState,
        IsSelected = focus.IsSelected,
        Value = focus.Value ?? string.Empty,
        IsPassword = focus.IsPassword,
        IsReadOnly = focus.IsValueReadOnly == true,
        PositionInSet = focus.PositionInSet,
        SizeOfSet = focus.SizeOfSet,
        Level = focus.Level,
        Invalid = focus.Invalid ?? string.Empty,
        Current = focus.Current ?? string.Empty,
        RuntimeId = focus.RuntimeId,
    };

    public static BrailleObject From(VBufferNode node) => new()
    {
        Name = node.Name,
        ControlType = node.ControlType,
        AriaRole = node.AriaRole,
        LandmarkType = node.LandmarkType,
        RoleDescription = node.RoleDescription,
        HeadingLevel = node.HeadingLevel,
        IsLink = node.IsLink,
        IsVisited = node.IsVisited,
        IsRequired = node.IsRequired,
        IsExpandable = node.IsExpandable,
        IsExpanded = node.IsExpanded,
        ToggleState = node.ToggleState,
        IsSelected = node.IsSelected,
        Value = node.Value,
        IsPassword = node.IsPassword,
        PositionInSet = node.PositionInSet,
        SizeOfSet = node.SizeOfSet,
        Level = node.Level,
        Invalid = node.Invalid,
        Current = node.Current,
        RuntimeId = node.UIARuntimeId,
    };

    /// <summary>The same object (by runtime id when both have one, else by role and name).</summary>
    public bool IsSameObject(BrailleObject other)
    {
        if (RuntimeId is { Length: > 0 } id && other.RuntimeId is { Length: > 0 } otherId)
            return id.AsSpan().SequenceEqual(otherId);
        return ControlType == other.ControlType && Name == other.Name;
    }
}
