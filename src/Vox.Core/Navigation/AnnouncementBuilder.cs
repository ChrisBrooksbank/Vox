using System.Text;
using Vox.Core.Buffer;
using Vox.Core.Configuration;

namespace Vox.Core.Navigation;

/// <summary>
/// Translates a <see cref="VBufferNode"/> and a <see cref="VerbosityProfile"/> into
/// a natural-language spoken announcement string.
///
/// Announcement order:
///   [heading level] [landmark type] [name] [control type] [visited] [required] [expanded/collapsed]
///
/// Each field is gated by the corresponding flag on <see cref="VerbosityProfile"/>.
/// </summary>
public sealed class AnnouncementBuilder
{
    /// <summary>
    /// Builds the spoken text for the given node at the given verbosity level.
    /// Returns an empty string if the node has no speakable content.
    /// </summary>
    public string Build(VBufferNode node, VerbosityProfile profile) =>
        Build(node, profile, announceVisitedLinks: true);

    /// <summary>
    /// Builds the spoken text, additionally honouring the AnnounceVisitedLinks setting.
    /// </summary>
    public string Build(VBufferNode node, VerbosityProfile profile, bool announceVisitedLinks)
    {
        var sb = new StringBuilder();

        // Heading level — "heading level 2"
        if (profile.AnnounceHeadingLevel && node.IsHeading)
        {
            Append(sb, $"heading level {node.HeadingLevel}");
        }

        // Landmark type — "navigation landmark"
        if (profile.AnnounceLandmarkType && node.IsLandmark)
        {
            Append(sb, $"{node.LandmarkType} landmark");
        }

        // Element name — always included (core content)
        if (!string.IsNullOrWhiteSpace(node.Name))
        {
            Append(sb, node.Name);
        }

        // Control type — "link", "button", "edit" (structural types such as Text are not spoken)
        if (profile.SpeaksRoleOf(node.ControlType, node.AriaRole, node.IsLink))
        {
            var controlType = ControlTypeNames.ToSpoken(node.ControlType);
            if (controlType is not null)
                Append(sb, controlType);
        }

        // Value — the text in a text box, a combo box's selection ("Country, combo box, France")
        var value = FormControls.SpokenValue(node);
        if (value is not null)
        {
            Append(sb, value);
        }

        // Visited state — "visited"
        if (announceVisitedLinks && profile.AnnounceVisitedState && node.IsLink && node.IsVisited)
        {
            Append(sb, "visited");
        }

        // Required state — "required"
        if (profile.AnnounceRequiredState && node.IsRequired)
        {
            Append(sb, "required");
        }

        // Expanded/collapsed state — "expanded" or "collapsed"
        if (profile.AnnounceExpandedState && node.IsExpandable)
        {
            Append(sb, node.IsExpanded ? "expanded" : "collapsed");
        }

        // Checked / selected state — essential, so announced at every verbosity with state info
        if (profile.AnnounceExpandedState)
        {
            var toggle = ToggleStateText(node.ToggleState);
            if (toggle is not null)
                Append(sb, toggle);
            else if (node.IsSelected is { } selected)
            {
                if (node.ControlType == "RadioButton")
                    Append(sb, selected ? "checked" : "not checked");
                else if (selected)
                    Append(sb, "selected");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds the spoken text for a focus change, using the same rules as virtual buffer nodes.
    /// </summary>
    public string Build(Pipeline.FocusChangedEvent focus, VerbosityProfile profile, bool announceVisitedLinks) =>
        Build(new VBufferNode
        {
            Name = focus.ElementName,
            ControlType = focus.ControlType,
            AriaRole = focus.AriaRole ?? string.Empty,
            HeadingLevel = focus.HeadingLevel,
            LandmarkType = focus.LandmarkType ?? string.Empty,
            IsLink = focus.IsLink,
            IsVisited = focus.IsVisited,
            IsRequired = focus.IsRequired,
            IsExpandable = focus.IsExpandable,
            IsExpanded = focus.IsExpanded,
            ToggleState = focus.ToggleState,
            IsSelected = focus.IsSelected,
            Value = focus.Value ?? string.Empty,
            IsPassword = focus.IsPassword,
        }, profile, announceVisitedLinks);

    /// <summary>Spoken text for a UIA ToggleState, or null when not a toggle.</summary>
    public static string? ToggleStateText(int? toggleState) => toggleState switch
    {
        ControlState.ToggleOff => "not checked",
        ControlState.ToggleOn => "checked",
        ControlState.ToggleIndeterminate => "half checked",
        _ => null,
    };

    /// <summary>
    /// Convenience overload: looks up the built-in profile for the given level.
    /// </summary>
    public string Build(VBufferNode node, VerbosityLevel level) =>
        Build(node, VerbosityProfile.For(level));

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void Append(StringBuilder sb, string text)
    {
        if (sb.Length > 0)
            sb.Append(", ");
        sb.Append(text);
    }
}
