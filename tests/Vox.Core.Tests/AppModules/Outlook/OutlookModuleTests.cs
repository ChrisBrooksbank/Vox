using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.AppModules;
using Vox.Core.AppModules.Outlook;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.AppModules.Outlook;

public class OutlookFormatterTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private static readonly DateTime Now = new(2026, 10, 14, 15, 0, 0); // a Wednesday

    public OutlookFormatterTests() => CultureInfo.CurrentCulture = new CultureInfo("en-GB");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private static OutlookMessage Message(bool unread = true, int attachments = 1) =>
        new(OutlookItemKind.Mail, "John Smith", "Lunch", new DateTime(2026, 10, 14, 10, 42, 0),
            Unread: unread, Flagged: true, Attachments: attachments, Size: 24_000);

    [Fact]
    public void Row_StatusesFirst_ThenColumnsInOrder() =>
        Assert.Equal("unread, flagged, has attachment, John Smith, Lunch, 10:42",
            OutlookFormatter.Row(Message(), OutlookFormatter.DefaultColumns, OutlookStatusPosition.First, Now));

    [Fact]
    public void Row_StatusesLast_OtherOrder() =>
        Assert.Equal("Lunch, John Smith, 23 KB, unread, flagged, has attachment",
            OutlookFormatter.Row(Message(), [OutlookColumn.Subject, OutlookColumn.From, OutlookColumn.Size], OutlookStatusPosition.Last, Now));

    [Fact]
    public void Row_StatusesOff_AndEmptyColumnsLeftOut() =>
        Assert.Equal("John Smith, no subject",
            OutlookFormatter.Row(Message() with { Subject = "", Received = null }, OutlookFormatter.DefaultColumns, OutlookStatusPosition.Off, Now));

    [Fact]
    public void Statuses_MeetingRequestAndImportance()
    {
        var meeting = new OutlookMessage(OutlookItemKind.MeetingRequest, "Ann", "Review", null, Importance: 2, MeetingResponse: 5);

        Assert.Equal(["meeting request, not answered", "high importance"], OutlookFormatter.Statuses(meeting));
        Assert.Equal(["3 attachments", "low importance"], OutlookFormatter.Statuses(Message(unread: false, attachments: 3) with { Flagged = false, Importance = 0 }));
    }

    [Fact]
    public void When_IsRelativeToNow()
    {
        Assert.Equal("09:05", OutlookFormatter.When(new DateTime(2026, 10, 14, 9, 5, 0), Now));
        Assert.Equal("yesterday 09:05", OutlookFormatter.When(new DateTime(2026, 10, 13, 9, 5, 0), Now));
        Assert.Equal("Saturday 09:05", OutlookFormatter.When(new DateTime(2026, 10, 10, 9, 5, 0), Now));
        Assert.Equal("1 September 2026", OutlookFormatter.When(new DateTime(2026, 9, 1, 9, 5, 0), Now));
    }

    [Theory]
    [InlineData(500, "500 bytes")]
    [InlineData(24_576, "24 KB")]
    [InlineData(1_572_864, "1.5 MB")]
    public void Size(long bytes, string expected) => Assert.Equal(expected, OutlookFormatter.Size(bytes));

    [Fact]
    public void Header()
    {
        var message = Message() with { To = "Me; You", AttachmentNames = ["a.pdf", "b.docx"] };

        Assert.Equal("From: John Smith", OutlookFormatter.Header(message, OutlookHeaderField.From));
        Assert.Equal("To: Me; You", OutlookFormatter.Header(message, OutlookHeaderField.To));
        Assert.Equal("No cc", OutlookFormatter.Header(message, OutlookHeaderField.Cc));
        Assert.Equal("Attachments: a.pdf, b.docx", OutlookFormatter.Header(message, OutlookHeaderField.Attachments));
        Assert.Equal("Date: Wednesday 14 October 2026 10:42", OutlookFormatter.Header(message, OutlookHeaderField.Date));
    }

    [Fact]
    public void Meeting()
    {
        var meeting = new OutlookMessage(OutlookItemKind.MeetingRequest, "Ann", "Review", null,
            MeetingResponse: 3, MeetingStart: new DateTime(2026, 10, 15, 14, 0, 0), MeetingLocation: "Room 2");

        Assert.Equal("Meeting Review, Thursday 15 October 2026 14:00, Room 2, accepted", OutlookFormatter.Meeting(meeting));
        Assert.Equal("Not a meeting request", OutlookFormatter.Meeting(Message()));
    }

    [Fact]
    public void Calendar()
    {
        var appointments = new List<OutlookAppointment>
        {
            new(new DateTime(2026, 10, 15, 9, 0, 0), new DateTime(2026, 10, 15, 9, 30, 0), "Standup", "Room 1", "Ann"),
            new(new DateTime(2026, 10, 14, 13, 0, 0), new DateTime(2026, 10, 14, 14, 0, 0), "Lunch"),
            new(new DateTime(2026, 10, 15, 0, 0, 0), new DateTime(2026, 10, 16, 0, 0, 0), "Holiday", AllDay: true),
        };

        Assert.Equal("3 appointments this week. Wednesday 14 October, 13:00 to 14:00, Lunch. Thursday 15 October, all day, Holiday. 09:00 to 09:30, Standup, Room 1, organized by Ann",
            OutlookFormatter.Calendar(appointments, "this week", multipleDays: true));
        Assert.Equal("No appointments today", OutlookFormatter.Calendar([], "today", false));
    }

    [Fact]
    public void ParseColumns_SkipsUnknownAndRepeated() =>
        Assert.Equal([OutlookColumn.Subject, OutlookColumn.From],
            OutlookFormatter.ParseColumns(["subject", "Nonsense", "From", "Subject", "99"]));
}

public class OutlookModuleTests : IDisposable
{
    private sealed class FakeOutlook : IOutlookSource
    {
        public OutlookMessage? Message { get; set; }
        public string? Body { get; set; }
        public List<OutlookAppointment> Appointments { get; } = [];
        public List<(DateTime, DateTime)> AppointmentQueries { get; } = [];
        public List<OutlookMeetingAnswer> Answers { get; } = [];

        public Task<OutlookMessage?> CurrentMessageAsync(IntPtr window) => Task.FromResult(Message);
        public Task<string?> BodyAsync(IntPtr window) => Task.FromResult(Body);

        public Task<IReadOnlyList<OutlookAppointment>?> AppointmentsAsync(DateTime from, DateTime to)
        {
            AppointmentQueries.Add((from, to));
            return Task.FromResult<IReadOnlyList<OutlookAppointment>?>(Appointments);
        }

        public Task<string?> AnswerMeetingAsync(IntPtr window, OutlookMeetingAnswer answer)
        {
            Answers.Add(answer);
            return Task.FromResult<string?>(OutlookFormatter.Answered(answer));
        }
    }

    private sealed class InlineSink : IEventSink
    {
        public void Post(ScreenReaderEvent evt)
        {
            if (evt is PipelineWorkEvent work)
                lock (this) work.Work();
        }
    }

    private static readonly DateTime Now = new(2026, 10, 14, 15, 0, 0);
    private readonly FakeOutlook _outlook = new();
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _speech;
    private readonly TestOptionsMonitor<VoxSettings> _settings = new(new VoxSettings { OutlookStatusPosition = OutlookStatusPosition.Off, OutlookColumns = ["From", "Subject"] });
    private readonly OutlookModule _module;

    public OutlookModuleTests()
    {
        _speech = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        _module = new OutlookModule(_outlook, _speech, new InlineSink(), _settings, now: () => Now);
        _module.OnActivated(new AppActivation(1, "OUTLOOK", new IntPtr(3)));
    }

    public void Dispose() => _speech.Dispose();

    private static FocusChangedEvent Row(string name) => new(DateTimeOffset.UtcNow, name, "DataItem");

    [Fact]
    public async Task MessageRow_IsReadInTheChosenColumnOrder()
    {
        _outlook.Message = new OutlookMessage(OutlookItemKind.Mail, "Ann", "Budget", Now);

        Assert.Null(_module.FormatFocusAnnouncement(Row("From Ann Subject Budget Received 15:00"), "From Ann Subject Budget"));

        await _engine.WaitForTextAsync("Ann, Budget");
    }

    [Fact]
    public async Task ListItemThatIsNotTheMessage_KeepsVoxsAnnouncement()
    {
        _outlook.Message = new OutlookMessage(OutlookItemKind.Mail, "Ann", "Budget", Now);

        _module.FormatFocusAnnouncement(Row("report.pdf"), "report.pdf, list item");

        await _engine.WaitForTextAsync("report.pdf, list item");
    }

    [Fact]
    public void OtherControls_KeepVoxsAnnouncement() =>
        Assert.Equal("Reply, button", _module.FormatFocusAnnouncement(new FocusChangedEvent(DateTimeOffset.UtcNow, "Reply", "Button"), "Reply, button"));

    [Fact]
    public async Task AutoRead_ReadsTheBodyAfterTheRow_OncePerMessage()
    {
        _settings.Set(_settings.CurrentValue with { OutlookAutoRead = true });
        _outlook.Message = new OutlookMessage(OutlookItemKind.Mail, "Ann", "Budget", Now);
        _outlook.Body = "Please see attached.";

        _module.FormatFocusAnnouncement(Row("Ann Budget"), "");
        await _engine.WaitForTextAsync("Please see attached.");

        _module.FormatFocusAnnouncement(Row("Ann Budget"), "");
        await _engine.WaitForAsync(s => _engine.SpokenText.Count(t => t == "Ann, Budget") == 2);
        await Task.Delay(100);
        Assert.Single(_engine.SpokenText, t => t == "Please see attached.");
    }

    [Fact]
    public async Task HeaderCommand()
    {
        _outlook.Message = new OutlookMessage(OutlookItemKind.Mail, "Ann", "Budget", Now);

        Assert.True(_module.TryHandle(NavigationCommand.OutlookReadSubject));

        await _engine.WaitForTextAsync("Subject: Budget");
    }

    [Fact]
    public async Task AnsweringAMeeting_NeedsASecondPress()
    {
        _module.TryHandle(NavigationCommand.OutlookDeclineMeeting);
        await _engine.WaitForTextAsync("Press again to decline and send the answer");
        Assert.Empty(_outlook.Answers);

        _module.TryHandle(NavigationCommand.OutlookDeclineMeeting);
        await _engine.WaitForTextAsync("Declined");
        Assert.Equal([OutlookMeetingAnswer.Decline], _outlook.Answers);
    }

    [Fact]
    public async Task Today_AsksForTodaysAppointments()
    {
        _module.TryHandle(NavigationCommand.OutlookToday);

        await _engine.WaitForTextAsync("No appointments today");
        Assert.Equal([(Now.Date, Now.Date.AddDays(1))], _outlook.AppointmentQueries);
    }

    [Fact]
    public void KeyMap_BindsEveryOutlookCommand()
    {
        var bound = AppModuleKeyMaps.Load("outlook").Select(b => b.Command).ToHashSet();
        var commands = Enum.GetValues<NavigationCommand>().Where(c => c.ToString().StartsWith("Outlook", StringComparison.Ordinal)).ToList();

        Assert.Equal(12, commands.Count);
        foreach (var command in commands)
        {
            Assert.Contains(command.ToString(), bound);
            Assert.True(_module.TryHandle(command));
        }
    }
}
