using Vox.Core.Input;

namespace Vox.Core.Pipeline;

public abstract record ScreenReaderEvent(DateTimeOffset Timestamp);

public record FocusChangedEvent(
    DateTimeOffset Timestamp,
    string ElementName,
    string ControlType,
    string? AriaRole = null,
    string? LandmarkType = null,
    int HeadingLevel = 0,
    bool IsLink = false,
    bool IsVisited = false,
    bool IsRequired = false,
    bool IsExpanded = false,
    bool IsExpandable = false
) : ScreenReaderEvent(Timestamp);

public record NavigationEvent(
    DateTimeOffset Timestamp,
    string Direction,
    string ElementName,
    string ControlType
) : ScreenReaderEvent(Timestamp);

public enum LiveRegionPoliteness { Polite, Assertive, Off }

public record LiveRegionChangedEvent(
    DateTimeOffset Timestamp,
    string Text,
    LiveRegionPoliteness Politeness,
    string? SourceId = null
) : ScreenReaderEvent(Timestamp);

public enum InteractionMode { Browse, Focus }

public record ModeChangedEvent(
    DateTimeOffset Timestamp,
    InteractionMode NewMode,
    string? Reason = null
) : ScreenReaderEvent(Timestamp);

public record TypingEchoEvent(
    DateTimeOffset Timestamp,
    string Text,
    bool IsWord
) : ScreenReaderEvent(Timestamp);

public record NavigationCommandEvent(
    DateTimeOffset Timestamp,
    NavigationCommand Command
) : ScreenReaderEvent(Timestamp);

public record RawKeyEvent(
    DateTimeOffset Timestamp,
    KeyEvent Key
) : ScreenReaderEvent(Timestamp);
