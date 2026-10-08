using System.Text;
using Vox.Core.Buffer;
using Vox.Core.Configuration;

namespace Vox.Core.Navigation;

/// <summary>
/// Translates a <see cref="VBufferNode"/> and a <see cref="VerbosityProfile"/> into
/// a natural-language spoken announcement string.
///
/// Announcement order:
///   [heading level] [landmark type] [name] [control type] [value] [visited] [required]
///   [expanded/collapsed] [checked/selected] [shortcut keys] [position] [description]
///
/// Each field is gated by the corresponding flag on <see cref="VerbosityProfile"/>.
/// </summary>
public sealed class AnnouncementBuilder
{
    /// <summary>
    /// When this returns true, checked, expanded and selected states are marked to be played as
    /// sounds (<see cref="Speech.StateSounds"/>) instead of spoken. Set at startup.
    /// </summary>
    public Func<bool>? StatesAsSounds { get; set; }
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
        bool sounds = StatesAsSounds?.Invoke() == true;
        string State(string cue, string word) => sounds ? Speech.StateSounds.Mark(cue, word) : word;

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
            // A link is spoken as a link whatever control type exposes it (e.g. role=link on a
            // generic element), as the cursor's role announcement does
            var controlType = ControlTypeNames.ToSpoken(node.IsLink ? "Hyperlink" : node.ControlType);
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
            Append(sb, node.IsExpanded
                ? State(Speech.StateSounds.Expanded, "expanded")
                : State(Speech.StateSounds.Collapsed, "collapsed"));
        }

        // Checked / selected state — essential, so announced at every verbosity regardless of
        // AnnounceExpandedState (which only governs the unrelated expanded/collapsed field above;
        // a profile that turns that off must not also silence whether a checkbox is checked)
        {
            var toggle = ToggleStateText(node.ToggleState);
            if (toggle is not null)
                Append(sb, State(node.ToggleState switch
                {
                    ControlState.ToggleOn => Speech.StateSounds.Checked,
                    ControlState.ToggleIndeterminate => Speech.StateSounds.HalfChecked,
                    _ => Speech.StateSounds.NotChecked,
                }, toggle));
            else if (node.IsSelected is { } selected)
            {
                if (node.ControlType == "RadioButton")
                    Append(sb, selected ? State(Speech.StateSounds.Checked, "checked") : State(Speech.StateSounds.NotChecked, "not checked"));
                else if (selected)
                    Append(sb, State(Speech.StateSounds.Selected, "selected"));
            }
        }

        // Shortcut keys — "Open, menu item, Ctrl+O"; the access key at the most verbose level
        if (!string.IsNullOrWhiteSpace(node.AcceleratorKey))
            Append(sb, node.AcceleratorKey.Trim());
        if (profile.AnnounceDescription && !string.IsNullOrWhiteSpace(node.AccessKey))
            Append(sb, node.AccessKey.Trim());

        // Position — "3 of 10", "level 2" (tree items, nested lists)
        if (profile.AnnouncePositionInfo)
        {
            if (node.PositionInSet > 0 && node.SizeOfSet >= node.PositionInSet)
                Append(sb, $"{node.PositionInSet} of {node.SizeOfSet}");
            if (node.Level > 0 && !node.IsHeading)
                Append(sb, $"level {node.Level}");
        }

        // Description — "Email, edit, We never share your address" (aria-description /
        // aria-describedby), unless it only repeats the name or value
        if (profile.AnnounceElementDescription)
        {
            var description = node.Description.Trim();
            if (description.Length > 0
                && !string.Equals(description, node.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                && !string.Equals(description, value?.Trim(), StringComparison.OrdinalIgnoreCase))
                Append(sb, description);
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
            // Links without an ARIA role (e.g. Firefox's) are still links: say "visited"
            IsLink = focus.IsLink || string.Equals(focus.ControlType, "Hyperlink", StringComparison.OrdinalIgnoreCase),
            IsVisited = focus.IsVisited,
            IsRequired = focus.IsRequired,
            IsExpandable = focus.IsExpandable,
            IsExpanded = focus.IsExpanded,
            ToggleState = focus.ToggleState,
            IsSelected = focus.IsSelected,
            Value = focus.Value ?? string.Empty,
            IsPassword = focus.IsPassword,
            PositionInSet = focus.PositionInSet,
            SizeOfSet = focus.SizeOfSet,
            Level = focus.Level,
            AcceleratorKey = focus.AcceleratorKey ?? string.Empty,
            AccessKey = focus.AccessKey ?? string.Empty,
            Description = focus.Description ?? string.Empty,
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
