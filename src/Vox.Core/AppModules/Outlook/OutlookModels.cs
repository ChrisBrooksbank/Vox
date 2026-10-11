namespace Vox.Core.AppModules.Outlook;

/// <summary>Columns of Outlook's message list Vox can read, in the order the user chooses.</summary>
public enum OutlookColumn
{
    From,
    Subject,
    Received,
    Size,
    Categories,
    To,
}

/// <summary>Where a message's statuses (unread, flagged, attachment, importance) are said.</summary>
public enum OutlookStatusPosition
{
    First,
    Last,
    Off,
}

/// <summary>Kinds of Outlook items Vox tells apart.</summary>
public enum OutlookItemKind
{
    Mail,
    MeetingRequest,
    MeetingResponse,
    MeetingCancellation,
    Other,
}

/// <summary>A message (or meeting item) as the message list and the header commands read it.</summary>
public sealed record OutlookMessage(
    OutlookItemKind Kind,
    string From,
    string Subject,
    DateTime? Received,
    bool Unread = false,
    bool Flagged = false,
    int Attachments = 0,
    int Importance = 1,
    long Size = 0,
    string? Categories = null,
    string? To = null,
    string? Cc = null,
    IReadOnlyList<string>? AttachmentNames = null,
    int MeetingResponse = 0,
    DateTime? MeetingStart = null,
    string? MeetingLocation = null);

/// <summary>A calendar appointment.</summary>
public sealed record OutlookAppointment(
    DateTime Start,
    DateTime End,
    string Subject,
    string? Location = null,
    string? Organizer = null,
    bool AllDay = false);

/// <summary>The header fields the header commands read, in their key order (Insert+Alt+1 is From).</summary>
public enum OutlookHeaderField
{
    From = 1,
    To,
    Cc,
    Subject,
    Date,
    Attachments,
}

/// <summary>How to answer a meeting request: Outlook's OlMeetingResponse values.</summary>
public enum OutlookMeetingAnswer
{
    Tentative = 2,
    Accept = 3,
    Decline = 4,
}
