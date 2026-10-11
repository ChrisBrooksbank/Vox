using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.AppModules.Outlook;

/// <summary>
/// Classic Outlook. A message list row taking focus is read from the object model, its columns in
/// the user's order and its statuses first or last, and with auto-read on the message follows.
/// Commands read header fields, a meeting request's state, today's and this week's appointments,
/// and answer meeting requests (pressed twice, as the answer is sent at once). New Outlook (olk)
/// is web content, read in browse mode.
/// </summary>
public sealed class OutlookModule : AppModule
{
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(2);

    private readonly IOutlookSource _source;
    private readonly SpeechQueue _speech;
    private readonly IEventSink _events;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly Func<DateTime> _now;
    private readonly ILogger _logger;

    // Pipeline thread only
    private long _sequence;
    private OutlookMessage? _lastMessage;
    private readonly Dictionary<OutlookMeetingAnswer, RepeatPressConfirmation> _confirmations = new();

    public OutlookModule(IOutlookSource source, SpeechQueue speech, IEventSink events, IOptionsMonitor<VoxSettings> settings,
        ILogger<OutlookModule>? logger = null, Func<DateTime>? now = null)
    {
        _source = source;
        _speech = speech;
        _events = events;
        _settings = settings;
        _now = now ?? (() => DateTime.Now);
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        foreach (var answer in Enum.GetValues<OutlookMeetingAnswer>())
            _confirmations[answer] = new RepeatPressConfirmation(ConfirmWindow);
    }

    public override string Id => "outlook";
    public override string Name => "Microsoft Outlook";
    public override IReadOnlyCollection<string> ProcessNames { get; } = ["OUTLOOK"];

    private IntPtr Window => Activation?.WindowHandle ?? IntPtr.Zero;

    private static readonly IReadOnlyDictionary<NavigationCommand, OutlookHeaderField> HeaderCommands =
        new Dictionary<NavigationCommand, OutlookHeaderField>
        {
            [NavigationCommand.OutlookReadFrom] = OutlookHeaderField.From,
            [NavigationCommand.OutlookReadTo] = OutlookHeaderField.To,
            [NavigationCommand.OutlookReadCc] = OutlookHeaderField.Cc,
            [NavigationCommand.OutlookReadSubject] = OutlookHeaderField.Subject,
            [NavigationCommand.OutlookReadDate] = OutlookHeaderField.Date,
            [NavigationCommand.OutlookReadAttachments] = OutlookHeaderField.Attachments,
        };

    private static readonly IReadOnlyDictionary<NavigationCommand, OutlookMeetingAnswer> AnswerCommands =
        new Dictionary<NavigationCommand, OutlookMeetingAnswer>
        {
            [NavigationCommand.OutlookAcceptMeeting] = OutlookMeetingAnswer.Accept,
            [NavigationCommand.OutlookTentativeMeeting] = OutlookMeetingAnswer.Tentative,
            [NavigationCommand.OutlookDeclineMeeting] = OutlookMeetingAnswer.Decline,
        };

    public override void OnActivated(AppActivation activation)
    {
        base.OnActivated(activation);
        _lastMessage = null;
    }

    /// <summary>Message list rows are list or grid items.</summary>
    public static bool IsRow(FocusChangedEvent focus) =>
        focus.ControlType is "ListItem" or "DataItem";

    public override string? FormatFocusAnnouncement(FocusChangedEvent focus, string announcement)
    {
        if (!IsRow(focus))
            return announcement;
        long sequence = ++_sequence;
        var window = Window;
        _ = Task.Run(async () =>
        {
            var message = await _source.CurrentMessageAsync(window).ConfigureAwait(false);
            _events.Post(new PipelineWorkEvent(DateTimeOffset.UtcNow, () => RowRead(sequence, focus, message, announcement)));
        });
        return null;
    }

    private void RowRead(long sequence, FocusChangedEvent focus, OutlookMessage? message, string announcement)
    {
        if (sequence != _sequence || !IsActive)
            return;
        // Only a row of the message list shows the selected message (an attachment or address
        // list item doesn't); its name has the subject in it
        if (message is null || string.IsNullOrWhiteSpace(message.Subject) || !focus.ElementName.Contains(message.Subject.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            Say(announcement, SpeechPriority.Interrupt);
            return;
        }
        var settings = _settings.CurrentValue;
        Say(OutlookFormatter.Row(message, OutlookFormatter.ParseColumns(settings.OutlookColumns), settings.OutlookStatusPosition, _now()),
            SpeechPriority.Interrupt);
        bool changed = _lastMessage is null || _lastMessage.Subject != message.Subject || _lastMessage.Received != message.Received;
        _lastMessage = message;
        if (changed && settings.OutlookAutoRead)
            ReadBody(sequence);
    }

    private void ReadBody(long sequence)
    {
        var window = Window;
        _ = Task.Run(async () =>
        {
            var body = await _source.BodyAsync(window).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(body))
                _events.Post(new PipelineWorkEvent(DateTimeOffset.UtcNow, () =>
                {
                    if (sequence == _sequence && IsActive)
                        Say(body.Trim(), SpeechPriority.Normal);
                }));
        });
    }

    public override bool TryHandle(NavigationCommand command)
    {
        var window = Window;
        if (HeaderCommands.TryGetValue(command, out var field))
        {
            Run(async () => await _source.CurrentMessageAsync(window).ConfigureAwait(false) is { } message
                ? OutlookFormatter.Header(message, field)
                : "No message");
            return true;
        }
        if (AnswerCommands.TryGetValue(command, out var answer))
        {
            if (!_confirmations[answer].Press())
            {
                Say($"Press again to {AnswerVerb(answer)} and send the answer", SpeechPriority.Interrupt);
                return true;
            }
            Run(async () => await _source.AnswerMeetingAsync(window, answer).ConfigureAwait(false) ?? "Outlook didn't answer");
            return true;
        }
        switch (command)
        {
            case NavigationCommand.OutlookMeetingStatus:
                Run(async () => await _source.CurrentMessageAsync(window).ConfigureAwait(false) is { } message
                    ? OutlookFormatter.Meeting(message)
                    : "No message");
                return true;
            case NavigationCommand.OutlookToday:
            {
                var today = _now().Date;
                Run(async () => await _source.AppointmentsAsync(today, today.AddDays(1)).ConfigureAwait(false) is { } appointments
                    ? OutlookFormatter.Calendar(appointments, "today", multipleDays: false)
                    : "The calendar isn't available");
                return true;
            }
            case NavigationCommand.OutlookThisWeek:
            {
                var today = _now().Date;
                Run(async () => await _source.AppointmentsAsync(today, today.AddDays(7)).ConfigureAwait(false) is { } appointments
                    ? OutlookFormatter.Calendar(appointments, "in the next 7 days", multipleDays: true)
                    : "The calendar isn't available");
                return true;
            }
        }
        return false;
    }

    private static string AnswerVerb(OutlookMeetingAnswer answer) => answer switch
    {
        OutlookMeetingAnswer.Accept => "accept",
        OutlookMeetingAnswer.Tentative => "tentatively accept",
        _ => "decline",
    };

    private void Run(Func<Task<string>> work) => _ = Task.Run(async () =>
    {
        try
        {
            Say(await work().ConfigureAwait(false), SpeechPriority.Interrupt);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Outlook command failed");
        }
    });

    private void Say(string text, SpeechPriority priority)
    {
        if (!string.IsNullOrWhiteSpace(text))
            _speech.Enqueue(new Utterance(text, priority));
    }
}
