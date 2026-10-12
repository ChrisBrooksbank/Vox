using System.Text;
using Microsoft.Extensions.Localization;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Localization;

namespace Vox.Core.Navigation;

/// <summary>
/// Translates a <see cref="VBufferNode"/> and a <see cref="VerbosityProfile"/> into
/// a natural-language spoken announcement string.
///
/// Announcement order:
///   [heading level] [landmark type] [name] [control type or role description] [value] [visited]
///   [required] [invalid entry] [current] [expanded/collapsed] [checked/pressed/selected] [sorted]
///   [shortcut keys] [position] [has details] [error message] [description]
///
/// Each field is gated by the corresponding flag on <see cref="VerbosityProfile"/>. Every word
/// Vox adds comes from the string resources (<see cref="VoxStrings"/>), in the language in use.
/// </summary>
public sealed class AnnouncementBuilder
{
    private readonly IStringLocalizer? _strings;

    /// <param name="strings">The strings to use; null: <see cref="VoxStrings.Localizer"/>.</param>
    public AnnouncementBuilder(IStringLocalizer<AnnouncementBuilder>? strings = null)
    {
        _strings = strings;
    }

    private IStringLocalizer L => _strings ?? VoxStrings.Localizer;

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
        var separator = L["Announce.Separator"].Value;
        void Append(StringBuilder sb, string text)
        {
            if (sb.Length > 0)
                sb.Append(separator);
            sb.Append(text);
        }
        bool sounds = StatesAsSounds?.Invoke() == true;
        string State(string cue, string word) => sounds ? Speech.StateSounds.Mark(cue, word) : word;

        // Heading level — "heading level 2"
        if (profile.AnnounceHeadingLevel && node.IsHeading)
        {
            Append(sb, L["Announce.HeadingLevel", node.HeadingLevel].Value);
        }

        // Landmark type — "navigation landmark"
        if (profile.AnnounceLandmarkType && node.IsLandmark)
        {
            Append(sb, L["Announce.Landmark", LandmarkName(node.LandmarkType)].Value);
        }

        // Element name — always included (core content)
        if (!string.IsNullOrWhiteSpace(node.Name))
        {
            Append(sb, node.Name);
        }

        // Control type — "link", "button", "edit" (structural types such as Text are not spoken),
        // or the author's role description ("slide")
        if (SpokenRole(node, profile) is { } role)
        {
            Append(sb, role);
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
            Append(sb, L["State.Visited"].Value);
        }

        // Required state — "required"
        if (profile.AnnounceRequiredState && node.IsRequired)
        {
            Append(sb, L["State.Required"].Value);
        }

        // Invalid entry — "invalid entry", "spelling error" (aria-invalid); essential at every verbosity
        if (AriaStates.InvalidText(node.Invalid) is { } invalid)
        {
            Append(sb, invalid);
        }

        // Current item — "current page" (aria-current)
        if (AriaStates.CurrentText(node.Current) is { } current)
        {
            Append(sb, current);
        }

        // Expanded/collapsed state — "expanded" or "collapsed"
        if (profile.AnnounceExpandedState && node.IsExpandable)
        {
            Append(sb, node.IsExpanded
                ? State(Speech.StateSounds.Expanded, L["State.Expanded"].Value)
                : State(Speech.StateSounds.Collapsed, L["State.Collapsed"].Value));
        }

        // Checked / selected state — essential, so announced at every verbosity regardless of
        // AnnounceExpandedState (which only governs the unrelated expanded/collapsed field above;
        // a profile that turns that off must not also silence whether a checkbox is checked)
        {
            // A toggle button (aria-pressed) is pressed or not pressed rather than checked
            var toggle = AriaStates.IsToggleButton(node.ControlType, node.AriaRole, node.ToggleState)
                ? AriaStates.PressedText(node.ToggleState)
                : ToggleStateText(node.ToggleState);
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
                    Append(sb, selected ? State(Speech.StateSounds.Checked, L["State.Checked"].Value) : State(Speech.StateSounds.NotChecked, L["State.NotChecked"].Value));
                else if (selected)
                    Append(sb, State(Speech.StateSounds.Selected, L["State.Selected"].Value));
            }
        }

        // Sort order of a column header — "sorted ascending" (aria-sort)
        if (AriaStates.SortText(node.Sort) is { } sort)
        {
            Append(sb, sort);
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
                Append(sb, L["Announce.Position", node.PositionInSet, node.SizeOfSet].Value);
            if (node.Level > 0 && !node.IsHeading)
                Append(sb, L["Announce.Level", node.Level].Value);
        }

        // Details elsewhere on the page — "has details" (aria-details)
        if (profile.AnnounceElementDescription && node.HasDetails)
        {
            Append(sb, L["State.HasDetails"].Value);
        }

        // Error message of an invalid entry — "Enter a valid email" (aria-errormessage)
        var errorMessage = node.Invalid.Length > 0 ? node.ErrorMessage.Trim() : string.Empty;
        if (errorMessage.Length > 0)
        {
            Append(sb, errorMessage);
        }

        // Description — "Email, edit, We never share your address" (aria-description /
        // aria-describedby), unless it only repeats the name, value or error message
        if (profile.AnnounceElementDescription)
        {
            var description = node.Description.Trim();
            if (description.Length > 0
                && !string.Equals(description, node.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                && !string.Equals(description, value?.Trim(), StringComparison.OrdinalIgnoreCase)
                && !string.Equals(description, errorMessage, StringComparison.OrdinalIgnoreCase))
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
            Invalid = focus.Invalid ?? string.Empty,
            ErrorMessage = focus.ErrorMessage ?? string.Empty,
            Current = focus.Current ?? string.Empty,
            Sort = focus.Sort ?? string.Empty,
            RoleDescription = focus.RoleDescription ?? string.Empty,
            HasDetails = focus.HasDetails,
        }, profile, announceVisitedLinks);

    /// <summary>
    /// The spoken role of a node when the profile speaks it: the author's role description
    /// (aria-roledescription) if any, else the control type's name ("link" for any link); null
    /// when nothing is spoken.
    /// </summary>
    public static string? SpokenRole(VBufferNode node, VerbosityProfile profile)
    {
        if (!profile.SpeaksRoleOf(node.ControlType, node.AriaRole, node.IsLink))
            return null;
        if (!string.IsNullOrWhiteSpace(node.RoleDescription))
            return node.RoleDescription.Trim();
        // A link is spoken as a link whatever control type exposes it (e.g. role=link on a
        // generic element), as the cursor's role announcement does
        return ControlTypeNames.ToSpoken(node.IsLink ? "Hyperlink" : node.ControlType);
    }

    /// <summary>Spoken text for a UIA ToggleState, or null when not a toggle.</summary>
    public static string? ToggleStateText(int? toggleState) => toggleState switch
    {
        ControlState.ToggleOff => VoxStrings.Get("State.NotChecked"),
        ControlState.ToggleOn => VoxStrings.Get("State.Checked"),
        ControlState.ToggleIndeterminate => VoxStrings.Get("State.HalfChecked"),
        _ => null,
    };

    /// <summary>
    /// Convenience overload: looks up the built-in profile for the given level.
    /// </summary>
    public string Build(VBufferNode node, VerbosityLevel level) =>
        Build(node, VerbosityProfile.For(level));

    /// <summary>
    /// A landmark's spoken name ("Navigation"): the buffer keeps the English name of its role,
    /// which is looked up in the strings (an unknown one is said as it is).
    /// </summary>
    public static string LandmarkName(string landmarkType) =>
        VoxStrings.GetOrDefault("Landmark." + landmarkType.Replace(" ", string.Empty), landmarkType);
}
