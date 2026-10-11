using System.Globalization;
using Vox.Core.AppModules.Office;

namespace Vox.Core.AppModules.Outlook;

/// <summary>Reads (and answers) Outlook items. Every method gives null when Outlook can't answer.</summary>
public interface IOutlookSource
{
    /// <summary>The open message's item, or the first selected in the message list.</summary>
    Task<OutlookMessage?> CurrentMessageAsync(IntPtr window);

    /// <summary>The current message's text.</summary>
    Task<string?> BodyAsync(IntPtr window);

    /// <summary>The default calendar's appointments overlapping <paramref name="from"/> to <paramref name="to"/>.</summary>
    Task<IReadOnlyList<OutlookAppointment>?> AppointmentsAsync(DateTime from, DateTime to);

    /// <summary>Answers the current meeting request and sends the answer; false when it isn't one.</summary>
    Task<string?> AnswerMeetingAsync(IntPtr window, OutlookMeetingAnswer answer);
}

/// <summary><see cref="IOutlookSource"/> through Outlook's object model (late bound, on the Office thread).</summary>
public sealed class OutlookObjectModelSource : IOutlookSource
{
    private const int MaxAppointments = 100;
    // OlObjectClass
    private const int ClassExplorer = 34;
    private const int ClassInspector = 35;
    private const int ClassMail = 43;
    private const int ClassMeetingRequest = 53;
    private const int ClassMeetingCancellation = 54;
    private const int ClassMeetingResponseNegative = 55;
    private const int ClassMeetingResponseTentative = 57;

    private readonly IOfficeObjectModel _objectModel;

    public OutlookObjectModelSource(IOfficeObjectModel objectModel)
    {
        _objectModel = objectModel;
    }

    public Task<OutlookMessage?> CurrentMessageAsync(IntPtr window) =>
        _objectModel.RunAsync<OutlookMessage>(OfficeApp.Outlook, window, app =>
            CurrentItem((object)app) is { } item ? Message(item) : null);

    public Task<string?> BodyAsync(IntPtr window) =>
        _objectModel.RunAsync<string>(OfficeApp.Outlook, window, app =>
            CurrentItem((object)app) is { } item ? (string)((dynamic)item).Body : null);

    public Task<IReadOnlyList<OutlookAppointment>?> AppointmentsAsync(DateTime from, DateTime to) =>
        _objectModel.RunAsync<IReadOnlyList<OutlookAppointment>>(OfficeApp.Outlook, IntPtr.Zero,
            app => Appointments((object)app, from, to), TimeSpan.FromSeconds(3));

    public Task<string?> AnswerMeetingAsync(IntPtr window, OutlookMeetingAnswer answer) =>
        _objectModel.RunAsync<string>(OfficeApp.Outlook, window, app =>
        {
            if (CurrentItem((object)app) is not { } current || (int)((dynamic)current).Class != ClassMeetingRequest)
                return "Not a meeting request";
            dynamic appointment = ((dynamic)current).GetAssociatedAppointment(false);
            // No dialog: the answer is sent as it is
            dynamic response = appointment.Respond((int)answer, true);
            response?.Send();
            return OutlookFormatter.Answered(answer);
        }, TimeSpan.FromSeconds(5));

    /// <summary>The open item when an inspector (message window) is in front, else the message list's selection.</summary>
    private static object? CurrentItem(object application)
    {
        dynamic app = application;
        dynamic window = app.ActiveWindow();
        if (window is null)
            return null;
        int windowClass = (int)window.Class;
        if (windowClass == ClassInspector)
            return window.CurrentItem;
        if (windowClass == ClassExplorer && (int)window.Selection.Count > 0)
            return window.Selection.Item(1);
        return null;
    }

    /// <summary>A mail or meeting item (or a fake of one) as Vox reads it.</summary>
    public static OutlookMessage Message(object source)
    {
        dynamic item = source;
        int itemClass = (int)item.Class;
        var kind = itemClass switch
        {
            ClassMail => OutlookItemKind.Mail,
            ClassMeetingRequest => OutlookItemKind.MeetingRequest,
            ClassMeetingCancellation => OutlookItemKind.MeetingCancellation,
            >= ClassMeetingResponseNegative and <= ClassMeetingResponseTentative => OutlookItemKind.MeetingResponse,
            _ => OutlookItemKind.Other,
        };
        var attachmentNames = new List<string>();
        int attachments = Try(() => (int)item.Attachments.Count);
        for (int i = 1; i <= attachments; i++)
            attachmentNames.Add(Try(() => (string)item.Attachments.Item(i).FileName) ?? $"Attachment {i}");

        int response = 0;
        DateTime? start = null;
        string? location = null;
        if (kind == OutlookItemKind.MeetingRequest)
        {
            object? appointment = Try<object>(() => item.GetAssociatedAppointment(false));
            if (appointment is not null)
            {
                dynamic meeting = appointment;
                response = Try(() => (int)meeting.ResponseStatus);
                start = Try<DateTime?>(() => (DateTime)meeting.Start);
                location = Try(() => (string)meeting.Location);
            }
        }

        return new OutlookMessage(
            kind,
            From: Try(() => (string)item.SenderName) ?? string.Empty,
            Subject: Try(() => (string)item.Subject) ?? string.Empty,
            Received: Try<DateTime?>(() => (DateTime)item.ReceivedTime),
            Unread: Try(() => (bool)item.UnRead),
            Flagged: Try(() => (int)item.FlagStatus == 2),
            Attachments: attachments,
            Importance: Try(() => (int?)item.Importance) ?? 1,
            Size: Try(() => Convert.ToInt64(item.Size)),
            Categories: Try(() => (string)item.Categories),
            To: Try(() => (string)item.To),
            Cc: Try(() => (string)item.CC),
            AttachmentNames: attachmentNames,
            MeetingResponse: response,
            MeetingStart: start,
            MeetingLocation: location);
    }

    private static IReadOnlyList<OutlookAppointment> Appointments(object application, DateTime from, DateTime to)
    {
        dynamic app = application;
        // olFolderCalendar
        dynamic items = app.GetNamespace("MAPI").GetDefaultFolder(9).Items;
        items.IncludeRecurrences = true;
        items.Sort("[Start]");
        // Restrict takes dates as the system writes them
        var culture = CultureInfo.CurrentCulture;
        dynamic found = items.Restrict(
            $"[Start] < '{to.ToString("g", culture)}' AND [End] > '{from.ToString("g", culture)}'");
        var appointments = new List<OutlookAppointment>();
        // With recurrences included Count isn't meaningful: walk with GetFirst/GetNext
        for (dynamic? item = found.GetFirst(); item is not null && appointments.Count < MaxAppointments; item = found.GetNext())
        {
            appointments.Add(new OutlookAppointment(
                (DateTime)item.Start, (DateTime)item.End,
                Try(() => (string)item.Subject) ?? string.Empty,
                Try(() => (string)item.Location),
                Try(() => (string)item.Organizer),
                Try(() => (bool)item.AllDayEvent)));
        }
        return appointments;
    }

    private static T? Try<T>(Func<T?> read)
    {
        try { return read(); }
        catch { return default; }
    }
}
