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
    public string Build(VBufferNode node, VerbosityProfile profile)
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

        // Control type — "link", "button", "edit"
        if (profile.AnnounceControlType && !string.IsNullOrWhiteSpace(node.ControlType))
        {
            // Avoid redundancy: don't announce "Heading" as a control type when heading level was already announced
            bool headingAlreadyAnnounced = profile.AnnounceHeadingLevel && node.IsHeading;
            bool isHeadingControlType = node.ControlType.Equals("Heading", StringComparison.OrdinalIgnoreCase);

            if (!(headingAlreadyAnnounced && isHeadingControlType))
            {
                Append(sb, node.ControlType.ToLowerInvariant());
            }
        }

        // Visited state — "visited"
        if (profile.AnnounceVisitedState && node.IsLink && node.IsVisited)
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

        return sb.ToString();
    }

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
