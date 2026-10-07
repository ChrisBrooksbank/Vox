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
    int[]? RuntimeId = null,
    bool IsPassword = false,
    int? ToggleState = null,
    bool? IsSelected = null,
    string? Value = null,
    bool? IsValueReadOnly = null
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

/// <summary>
/// A polite live region's cooldown has ended; announce whatever was held back for it.
/// Posted by the pipeline itself.
/// </summary>
public record LiveRegionFlushEvent(
    DateTimeOffset Timestamp,
    string SourceId
) : ScreenReaderEvent(Timestamp);

public enum InteractionMode { Browse, Focus }

/// <summary>
/// The browse/focus mode changed. The mode cue always plays; <paramref name="Announce"/> says
/// whether the new mode is also spoken (user toggles) or not (automatic switches, whose cue must
/// not talk over the announcement of the field that caused them).
/// </summary>
public record ModeChangedEvent(
    DateTimeOffset Timestamp,
    InteractionMode NewMode,
    string? Reason = null,
    bool Announce = true
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

/// <summary>
/// A UIA notification (IUIAutomation5). <paramref name="Processing"/> is the UIA
/// NotificationProcessing value: 0 ImportantAll, 1 ImportantMostRecent, 2 All, 3 MostRecent,
/// 4 CurrentThenMostRecent. <paramref name="IsFromForeground"/> is false for notifications raised
/// by background applications.
/// </summary>
public record NotificationEvent(
    DateTimeOffset Timestamp,
    string? ActivityId,
    string? NotificationText,
    int Processing = 2,
    bool IsFromForeground = true
) : ScreenReaderEvent(Timestamp);

/// <summary>
/// The coalescing delay for a "most recent" notification activity has passed; speak its latest text.
/// Posted by the pipeline itself.
/// </summary>
public record NotificationFlushEvent(
    DateTimeOffset Timestamp,
    string ActivityId
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
/// Posted by the document tracker when the focused element turned out to be inside the current
/// document although the buffer may not contain it yet (e.g. a newly added dialog).
/// </summary>
public record FocusInDocumentEvent(
    DateTimeOffset Timestamp,
    int[] DocumentRuntimeId,
    int[] FocusedRuntimeId,
    long FocusSequence = 0
) : ScreenReaderEvent(Timestamp);

/// <summary>
/// A subtree of the active document was re-captured after a structure change.
/// <paramref name="NewSubtree"/> is null when the element no longer exists.
/// <paramref name="DocumentRuntimeId"/> identifies the document it was captured from, so updates
/// for a document that has since been replaced can be ignored. <paramref name="AncestorRuntimeIds"/>
/// lists the element's ancestors (nearest first) for splicing elements the buffer doesn't know yet.
/// </summary>
public record SubtreeChangedEvent(
    DateTimeOffset Timestamp,
    int[] RuntimeId,
    IVBufferElement? NewSubtree,
    int[]? DocumentRuntimeId = null,
    IReadOnlyList<int[]>? AncestorRuntimeIds = null
) : ScreenReaderEvent(Timestamp);

/// <summary>
/// The Elements List dialog closed; <paramref name="SelectedNode"/> is null if it was cancelled.
/// </summary>
public record ElementsListClosedEvent(
    DateTimeOffset Timestamp,
    VBufferNode? SelectedNode
) : ScreenReaderEvent(Timestamp);

/// <summary>
/// An item in the active document was selected (UIA SelectionItem_ElementSelected).
/// </summary>
public record ElementSelectedEvent(
    DateTimeOffset Timestamp,
    int[] RuntimeId,
    string Name
) : ScreenReaderEvent(Timestamp);

/// <summary>
/// UIA calls to an application are timing out: it isn't responding. Spoken as "{AppName} not responding".
/// </summary>
public record AppNotRespondingEvent(
    DateTimeOffset Timestamp,
    int ProcessId,
    string AppName
) : ScreenReaderEvent(Timestamp);

/// <summary>
/// The caret or selection moved in the focused text control (UIA TextSelectionChanged).
/// </summary>
public record CaretMovedEvent(
    DateTimeOffset Timestamp,
    int[] RuntimeId
) : ScreenReaderEvent(Timestamp);

/// <summary>
/// The text of the focused text control changed (UIA TextChanged).
/// </summary>
public record TextEditedEvent(
    DateTimeOffset Timestamp,
    int[] RuntimeId
) : ScreenReaderEvent(Timestamp);

/// <summary>Focus moved into another top-level window (its handle, title and process).</summary>
public record ForegroundWindowChangedEvent(
    DateTimeOffset Timestamp,
    IntPtr WindowHandle,
    string Title,
    int ProcessId
) : ScreenReaderEvent(Timestamp);
