using System.Globalization;

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
                OutlookColumn.Subject => string.IsNullOrWhiteSpace(message.Subject) ? "no subject" : message.Subject,
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
                status.Add($"meeting request, {ResponseName(message.MeetingResponse)}");
                break;
            case OutlookItemKind.MeetingResponse:
                status.Add("meeting response");
                break;
            case OutlookItemKind.MeetingCancellation:
                status.Add("meeting cancelled");
                break;
        }
        if (message.Unread)
            status.Add("unread");
        if (message.Flagged)
            status.Add("flagged");
        if (message.Attachments > 0)
            status.Add(message.Attachments == 1 ? "has attachment" : $"{message.Attachments} attachments");
        if (message.Importance == 2)
            status.Add("high importance");
        else if (message.Importance == 0)
            status.Add("low importance");
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
            return $"yesterday {clock}";
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
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{Math.Max(1, bytes / 1024)} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
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
        var name = field == OutlookHeaderField.Date ? "Date" : field.ToString();
        return string.IsNullOrWhiteSpace(value) ? $"No {name.ToLowerInvariant()}" : $"{name}: {value.Trim()}";
    }

    /// <summary>A meeting request's state: when, where and the user's answer so far.</summary>
    public static string Meeting(OutlookMessage message)
    {
        if (message.Kind != OutlookItemKind.MeetingRequest)
            return "Not a meeting request";
        var parts = new List<string> { $"Meeting {message.Subject}".Trim() };
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
        1 => "you organized it",
        2 => "tentatively accepted",
        3 => "accepted",
        4 => "declined",
        _ => "not answered",
    };

    /// <summary>What is said when an answer has been sent.</summary>
    public static string Answered(OutlookMeetingAnswer answer) => answer switch
    {
        OutlookMeetingAnswer.Accept => "Accepted",
        OutlookMeetingAnswer.Tentative => "Tentatively accepted",
        _ => "Declined",
    };

    /// <summary>
    /// A calendar summary: "3 appointments today. 9:00 to 9:30, Standup, Room 1, organized by Ann. ...".
    /// Days are named when the summary covers more than one.
    /// </summary>
    public static string Calendar(IReadOnlyList<OutlookAppointment> appointments, string period, bool multipleDays)
    {
        if (appointments.Count == 0)
            return $"No appointments {period}";
        var culture = CultureInfo.CurrentCulture;
        var parts = new List<string> { appointments.Count == 1 ? $"1 appointment {period}" : $"{appointments.Count} appointments {period}" };
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
                ? "all day"
                : $"{appointment.Start.ToString("t", culture)} to {appointment.End.ToString("t", culture)}");
            items.Add(string.IsNullOrWhiteSpace(appointment.Subject) ? "no subject" : appointment.Subject.Trim());
            if (!string.IsNullOrWhiteSpace(appointment.Location))
                items.Add(appointment.Location.Trim());
            if (!string.IsNullOrWhiteSpace(appointment.Organizer))
                items.Add($"organized by {appointment.Organizer.Trim()}");
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
