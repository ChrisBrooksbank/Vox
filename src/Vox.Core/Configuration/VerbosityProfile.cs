namespace Vox.Core.Configuration;

/// <summary>
/// Defines which announcement fields are included at a given verbosity level.
/// </summary>
public sealed class VerbosityProfile
{
    public VerbosityLevel Level { get; }
    public bool AnnounceHeadingLevel { get; }
    public bool AnnounceLandmarkType { get; }
    public bool AnnounceControlType { get; }
    public bool AnnounceVisitedState { get; }
    public bool AnnounceRequiredState { get; }
    public bool AnnounceExpandedState { get; }
    public bool AnnouncePositionInfo { get; }
    public bool AnnounceDescription { get; }

    private VerbosityProfile(
        VerbosityLevel level,
        bool announceHeadingLevel,
        bool announceLandmarkType,
        bool announceControlType,
        bool announceVisitedState,
        bool announceRequiredState,
        bool announceExpandedState,
        bool announcePositionInfo,
        bool announceDescription)
    {
        Level = level;
        AnnounceHeadingLevel = announceHeadingLevel;
        AnnounceLandmarkType = announceLandmarkType;
        AnnounceControlType = announceControlType;
        AnnounceVisitedState = announceVisitedState;
        AnnounceRequiredState = announceRequiredState;
        AnnounceExpandedState = announceExpandedState;
        AnnouncePositionInfo = announcePositionInfo;
        AnnounceDescription = announceDescription;
    }

    /// <summary>
    /// Beginner: Everything announced.
    /// Example: "heading level 2, navigation landmark, Products, link, visited"
    /// </summary>
    public static readonly VerbosityProfile Beginner = new(
        level: VerbosityLevel.Beginner,
        announceHeadingLevel: true,
        announceLandmarkType: true,
        announceControlType: true,
        announceVisitedState: true,
        announceRequiredState: true,
        announceExpandedState: true,
        announcePositionInfo: true,
        announceDescription: true);

    /// <summary>
    /// Intermediate: Control type + essential state.
    /// Example: "heading level 2, Products, link, visited"
    /// </summary>
    public static readonly VerbosityProfile Intermediate = new(
        level: VerbosityLevel.Intermediate,
        announceHeadingLevel: true,
        announceLandmarkType: false,
        announceControlType: true,
        announceVisitedState: true,
        announceRequiredState: true,
        announceExpandedState: true,
        announcePositionInfo: false,
        announceDescription: false);

    /// <summary>
    /// Advanced: Minimal — only role when ambiguous.
    /// Example: "Products"
    /// </summary>
    public static readonly VerbosityProfile Advanced = new(
        level: VerbosityLevel.Advanced,
        announceHeadingLevel: false,
        announceLandmarkType: false,
        announceControlType: false,
        announceVisitedState: false,
        announceRequiredState: false,
        announceExpandedState: true,
        announcePositionInfo: false,
        announceDescription: false);

    /// <summary>
    /// Returns the built-in profile for the given verbosity level.
    /// </summary>
    public static VerbosityProfile For(VerbosityLevel level) => level switch
    {
        VerbosityLevel.Beginner => Beginner,
        VerbosityLevel.Intermediate => Intermediate,
        VerbosityLevel.Advanced => Advanced,
        _ => Beginner
    };
}
