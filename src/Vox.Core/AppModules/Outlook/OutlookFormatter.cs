using System.Globalization;
using Vox.Core.Localization;

namespace Vox.Core.AppModules.Outlook;

/// <summary>Composes what is said in Outlook: message list rows, header fields, meeting states and calendar summaries.</summary>
public static class OutlookFormatter
{
    /// <summary>The columns read when the user hasn't chosen any.</summary>
    public static readonly IReadOnlyList<OutlookColumn> DefaultColumns =
        [OutlookColumn.From, OutlookColumn.Subject, OutlookColumn.Received];

    /// <summary>
    /// A message list row: its statuses first or last (or not at all), and the chosen columns in
    /// their order. "Unread, flagged, has attachment, John Smith, Lunch, 10:42". A column with
    /// nothing in it is left out.
    /// </summary>
    public static string Row(OutlookMessage message, IReadOnlyList<OutlookColumn> columns, OutlookStatusPosition statuses, DateTime now)
    {
        var parts = new List<string>();
        var status = Statuses(message);
        if (statuses == OutlookStatusPosition.First)
            parts.AddRange(status);
        foreach (var column in columns.Count == 0 ? DefaultColumns : columns)
        {
            var value = column switch
            {
                OutlookColumn.From => message.From,
                OutlookColumn.Subject => string.IsNullOrWhiteSpace(message.Subject) ? VoxStrings.Get("Outlook.NoSubject") : message.Subject,
                OutlookColumn.Received => message.Received is { } received ? When(received, now) : null,
                OutlookColumn.Size => message.Size > 0 ? Size(message.Size) : null,
                OutlookColumn.Categories => message.Categories,
                OutlookColumn.To => message.To,
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add(value.Trim());
        }
        if (statuses == OutlookStatusPosition.Last)
            parts.AddRange(status);
        return string.Join(", ", parts);
    }

    /// <summary>What sets the message apart: kind, unread, flagged, attachments, importance.</summary>
    public static IReadOnlyList<string> Statuses(OutlookMessage message)
    {
        var status = new List<string>();
        switch (message.Kind)
        {
            case OutlookItemKind.MeetingRequest:
                status.Add(VoxStrings.Format("Outlook.MeetingRequestStatus", ResponseName(message.MeetingResponse)));
                break;
            case OutlookItemKind.MeetingResponse:
                status.Add(VoxStrings.Get("Outlook.MeetingResponse"));
                break;
            case OutlookItemKind.MeetingCancellation:
                status.Add(VoxStrings.Get("Outlook.MeetingCancelled"));
                break;
        }
        if (message.Unread)
            status.Add(VoxStrings.Get("Outlook.Unread"));
        if (message.Flagged)
            status.Add(VoxStrings.Get("Outlook.Flagged"));
        if (message.Attachments > 0)
            status.Add(message.Attachments == 1 ? VoxStrings.Get("Outlook.HasAttachment") : VoxStrings.Format("Outlook.Attachments", message.Attachments));
        if (message.Importance == 2)
            status.Add(VoxStrings.Get("Outlook.HighImportance"));
        else if (message.Importance == 0)
            status.Add(VoxStrings.Get("Outlook.LowImportance"));
        return status;
    }

    /// <summary>"10:42" today, "yesterday 10:42", "Mon 10:42" this week, else the date.</summary>
    public static string When(DateTime time, DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        var clock = time.ToString("t", culture);
        if (time.Date == now.Date)
            return clock;
        if (time.Date == now.Date.AddDays(-1))
            return VoxStrings.Format("Outlook.Yesterday", clock);
        if (time.Date > now.Date.AddDays(-7) && time.Date < now.Date)
            return $"{time.ToString("dddd", culture)} {clock}";
        return time.ToString("d MMMM yyyy", culture);
    }

    /// <summary>"Wednesday 14 October 2026 10:42".</summary>
    public static string FullDate(DateTime time) =>
        $"{time.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture)} {time.ToString("t", CultureInfo.CurrentCulture)}";

    /// <summary>"24 KB", "1.5 MB".</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => VoxStrings.Format("Outlook.SizeBytes", bytes),
        < 1024 * 1024 => VoxStrings.Format("Outlook.SizeKilobytes", Math.Max(1, bytes / 1024)),
        _ => VoxStrings.Format("Outlook.SizeMegabytes", bytes / (1024.0 * 1024.0)),
    };

    /// <summary>A header field as the header commands say it: "From: John Smith".</summary>
    public static string Header(OutlookMessage message, OutlookHeaderField field)
    {
        var value = field switch
        {
            OutlookHeaderField.From => message.From,
            OutlookHeaderField.To => message.To,
            OutlookHeaderField.Cc => message.Cc,
            OutlookHeaderField.Subject => message.Subject,
            OutlookHeaderField.Date => message.Received is { } received ? FullDate(received) : null,
            _ => message.AttachmentNames is { Count: > 0 } names ? string.Join(", ", names) : null,
        };
        if (string.IsNullOrWhiteSpace(value))
        {
            return field switch
            {
                OutlookHeaderField.From => VoxStrings.Get("Outlook.NoFrom"),
                OutlookHeaderField.To => VoxStrings.Get("Outlook.NoTo"),
                OutlookHeaderField.Cc => VoxStrings.Get("Outlook.NoCc"),
                OutlookHeaderField.Subject => VoxStrings.Get("Outlook.NoSubjectHeader"),
                OutlookHeaderField.Date => VoxStrings.Get("Outlook.NoDate"),
                _ => VoxStrings.Get("Outlook.NoAttachments"),
            };
        }
        return field switch
        {
            OutlookHeaderField.From => VoxStrings.Format("Outlook.HeaderFrom", value.Trim()),
            OutlookHeaderField.To => VoxStrings.Format("Outlook.HeaderTo", value.Trim()),
            OutlookHeaderField.Cc => VoxStrings.Format("Outlook.HeaderCc", value.Trim()),
            OutlookHeaderField.Subject => VoxStrings.Format("Outlook.HeaderSubject", value.Trim()),
            OutlookHeaderField.Date => VoxStrings.Format("Outlook.HeaderDate", value.Trim()),
            _ => VoxStrings.Format("Outlook.HeaderAttachments", value.Trim()),
        };
    }

    /// <summary>A meeting request's state: when, where and the user's answer so far.</summary>
    public static string Meeting(OutlookMessage message)
    {
        if (message.Kind != OutlookItemKind.MeetingRequest)
            return VoxStrings.Get("Outlook.NotMeetingRequest");
        var parts = new List<string> { VoxStrings.Format("Outlook.Meeting", message.Subject).Trim() };
        if (message.MeetingStart is { } start)
            parts.Add(FullDate(start));
        if (!string.IsNullOrWhiteSpace(message.MeetingLocation))
            parts.Add(message.MeetingLocation.Trim());
        parts.Add(ResponseName(message.MeetingResponse));
        return string.Join(", ", parts);
    }

    /// <summary>A name for Outlook's OlResponseStatus.</summary>
    public static string ResponseName(int responseStatus) => responseStatus switch
    {
        1 => VoxStrings.Get("Outlook.Response.Organizer"),
        2 => VoxStrings.Get("Outlook.Response.Tentative"),
        3 => VoxStrings.Get("Outlook.Response.Accepted"),
        4 => VoxStrings.Get("Outlook.Response.Declined"),
        _ => VoxStrings.Get("Outlook.Response.None"),
    };

    /// <summary>What is said when an answer has been sent.</summary>
    public static string Answered(OutlookMeetingAnswer answer) => answer switch
    {
        OutlookMeetingAnswer.Accept => VoxStrings.Get("Outlook.Answered.Accept"),
        OutlookMeetingAnswer.Tentative => VoxStrings.Get("Outlook.Answered.Tentative"),
        _ => VoxStrings.Get("Outlook.Answered.Decline"),
    };

    /// <summary>
    /// A calendar summary: "3 appointments today. 9:00 to 9:30, Standup, Room 1, organized by Ann. ...".
    /// Days are named when the summary covers more than one.
    /// </summary>
    public static string Calendar(IReadOnlyList<OutlookAppointment> appointments, string period, bool multipleDays)
    {
        if (appointments.Count == 0)
            return VoxStrings.Format("Outlook.NoAppointments", period);
        var culture = CultureInfo.CurrentCulture;
        var parts = new List<string> { appointments.Count == 1 ? VoxStrings.Format("Outlook.OneAppointment", period) : VoxStrings.Format("Outlook.Appointments", appointments.Count, period) };
        DateTime? day = null;
        foreach (var appointment in appointments.OrderBy(a => a.Start))
        {
            var items = new List<string>();
            if (multipleDays && appointment.Start.Date != day)
            {
                day = appointment.Start.Date;
                items.Add(appointment.Start.ToString("dddd d MMMM", culture));
            }
            items.Add(appointment.AllDay
                ? VoxStrings.Get("Outlook.AllDay")
                : VoxStrings.Format("Outlook.TimeRange", appointment.Start.ToString("t", culture), appointment.End.ToString("t", culture)));
            items.Add(string.IsNullOrWhiteSpace(appointment.Subject) ? VoxStrings.Get("Outlook.NoSubject") : appointment.Subject.Trim());
            if (!string.IsNullOrWhiteSpace(appointment.Location))
                items.Add(appointment.Location.Trim());
            if (!string.IsNullOrWhiteSpace(appointment.Organizer))
                items.Add(VoxStrings.Format("Outlook.OrganizedBy", appointment.Organizer.Trim()));
            parts.Add(string.Join(", ", items));
        }
        return string.Join(". ", parts);
    }

    /// <summary>The columns of the <c>OutlookColumns</c> setting ("From, Subject, Received"); unknown names are skipped.</summary>
    public static IReadOnlyList<OutlookColumn> ParseColumns(IEnumerable<string> names)
    {
        var columns = new List<OutlookColumn>();
        foreach (var name in names)
        {
            if (Enum.TryParse<OutlookColumn>(name.Trim(), ignoreCase: true, out var column) && Enum.IsDefined(column) && !columns.Contains(column))
                columns.Add(column);
        }
        return columns;
    }
}
