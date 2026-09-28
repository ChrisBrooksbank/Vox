using Vox.Core.Buffer;
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
    bool IsExpandable = false,
    int[]? RuntimeId = null
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

/// <summary>
/// The children of the element with <paramref name="RuntimeId"/> changed (UIA StructureChanged sender).
/// </summary>
public record StructureChangedEvent(
    DateTimeOffset Timestamp,
    int[] RuntimeId
) : ScreenReaderEvent(Timestamp);

public record PropertyChangedEvent(
    DateTimeOffset Timestamp,
    int[] RuntimeId,
    int PropertyId,
    object? NewValue
) : ScreenReaderEvent(Timestamp);

public record NotificationEvent(
    DateTimeOffset Timestamp,
    string? ActivityId,
    string? NotificationText
) : ScreenReaderEvent(Timestamp);

/// <summary>
/// A web document gained focus (<paramref name="Document"/> is its new virtual buffer)
/// or focus left web content (<paramref name="Document"/> is null).
/// </summary>
public record DocumentChangedEvent(
    DateTimeOffset Timestamp,
    VBufferDocument? Document,
    int[]? FocusedRuntimeId = null
) : ScreenReaderEvent(Timestamp);

/// <summary>
/// A subtree of the active document was re-captured after a structure change.
/// <paramref name="NewSubtree"/> is null when the element no longer exists.
/// </summary>
public record SubtreeChangedEvent(
    DateTimeOffset Timestamp,
    int[] RuntimeId,
    IVBufferElement? NewSubtree
) : ScreenReaderEvent(Timestamp);

/// <summary>
/// The Elements List dialog closed; <paramref name="SelectedNode"/> is null if it was cancelled.
/// </summary>
public record ElementsListClosedEvent(
    DateTimeOffset Timestamp,
    VBufferNode? SelectedNode
) : ScreenReaderEvent(Timestamp);
