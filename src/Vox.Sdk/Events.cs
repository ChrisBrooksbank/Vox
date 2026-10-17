namespace Vox.Sdk;

/// <summary>Something Vox handled, as add-ons see it. New kinds may be added in a minor version; treat unknown ones as <see cref="OtherEvent"/>.</summary>
public abstract record VoxEvent(DateTimeOffset Timestamp);

/// <summary>
/// The focused object. <see cref="Value"/> is null for password fields. An add-on may read what is
/// focused but must never log or send it anywhere.
/// </summary>
public sealed record FocusInfo(string Name, string ControlType, string? Role = null, string? Value = null,
    string? Description = null, bool IsPassword = false);

/// <summary>Focus moved to <see cref="Focus"/>.</summary>
public sealed record FocusEvent(DateTimeOffset Timestamp, FocusInfo Focus) : VoxEvent(Timestamp);

/// <summary>A top-level window of process <see cref="ProcessId"/> came to the foreground.</summary>
public sealed record ForegroundWindowEvent(DateTimeOffset Timestamp, string Title, int ProcessId) : VoxEvent(Timestamp);

/// <summary>An application raised a notification (UIA notification event).</summary>
public sealed record NotificationEvent(DateTimeOffset Timestamp, string Text, string? ActivityId) : VoxEvent(Timestamp);

/// <summary>A live region on a web page changed.</summary>
public sealed record LiveRegionEvent(DateTimeOffset Timestamp, string Text, bool IsAssertive) : VoxEvent(Timestamp);

/// <summary>The caret or selection moved in the focused text control.</summary>
public sealed record CaretMovedEvent(DateTimeOffset Timestamp) : VoxEvent(Timestamp)
{
    /// <summary>The line at the caret; null for a password field.</summary>
    public string? Line { get; init; }

    /// <summary>The word at the caret; null for a password field.</summary>
    public string? Word { get; init; }

    /// <summary>The character at the caret; null for a password field.</summary>
    public string? Character { get; init; }
}

/// <summary>The focused text control's text changed.</summary>
public sealed record TextChangedEvent(DateTimeOffset Timestamp) : VoxEvent(Timestamp);

/// <summary>Any other event, by Vox's name for its kind ("PropertyChanged", "MenuEvent", ...).</summary>
public sealed record OtherEvent(DateTimeOffset Timestamp, string Kind) : VoxEvent(Timestamp);
