using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.Configuration;
using Vox.Core.Localization;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.AppModules.Explorer;

/// <summary>Composes a details-view row: "report.docx, Date modified: 3 May, Type: Word document, 3 of 10".</summary>
public static class DetailsRowFormatter
{
    /// <summary>
    /// The first cell (the name) is said alone; the others with their column's header when
    /// <paramref name="sayHeaders"/>. Empty cells are left out. Null when the row has no details
    /// (fewer than two cells, or no headers: not a details view).
    /// </summary>
    public static string? Format(IReadOnlyList<DetailsCell> cells, bool sayHeaders, int position = 0, int count = 0)
    {
        if (cells.Count < 2 || cells.All(c => string.IsNullOrWhiteSpace(c.Header)))
            return null;
        var parts = new List<string>();
        for (int i = 0; i < cells.Count; i++)
        {
            var value = Clean(cells[i].Value);
            if (value.Length == 0)
                continue;
            var header = Clean(cells[i].Header);
            parts.Add(i == 0 || !sayHeaders || header.Length == 0 ? value : VoxStrings.Format("Explorer.HeaderValue", header, value));
        }
        if (parts.Count == 0)
            return null;
        if (position > 0 && count > 0)
            parts.Add(VoxStrings.Format("Announce.Position", position, count));
        return string.Join(", ", parts);
    }

    // Explorer marks dates with invisible left-to-right marks
    private static string Clean(string text) =>
        text.Replace("‎", string.Empty).Replace("‏", string.Empty).Trim();
}

/// <summary>
/// File Explorer. In a details view each item is read with its columns (and, as a setting, their
/// headers) instead of its name alone. Other list items (icons view, the desktop) keep Vox's
/// announcement. Explorer is also the shell (taskbar, Alt+Tab): only list items are changed.
/// </summary>
public sealed class ExplorerModule : AppModule
{
    private readonly IDetailsRowSource _rows;
    private readonly SpeechQueue _speech;
    private readonly IEventSink _events;
    private readonly IOptionsMonitor<VoxSettings> _settings;

    // Pipeline thread only
    private long _sequence;
    private bool _onItem;

    public ExplorerModule(IDetailsRowSource rows, SpeechQueue speech, IEventSink events, IOptionsMonitor<VoxSettings> settings)
    {
        _rows = rows;
        _speech = speech;
        _events = events;
        _settings = settings;
    }

    public override string Id => "explorer";
    public override string Name => VoxStrings.Get("Explorer.Name");
    public override IReadOnlyCollection<string> ProcessNames { get; } = ["explorer"];

    public override void OnActivated(AppActivation activation)
    {
        base.OnActivated(activation);
        // A row read before Explorer was left is stale
        _sequence++;
        _onItem = false;
    }

    public override string? FormatFocusAnnouncement(FocusChangedEvent focus, string announcement)
    {
        if (focus.ControlType != "ListItem")
        {
            // A row read still on its way would be said over this control (F2's rename box)
            _sequence++;
            _onItem = false;
            return announcement;
        }
        // From item to item cuts the last one off; the first is queued after the window's title
        var priority = _onItem ? SpeechPriority.Interrupt : SpeechPriority.High;
        _onItem = true;
        long sequence = ++_sequence;
        _ = Task.Run(async () =>
        {
            var cells = await _rows.FocusedRowAsync().ConfigureAwait(false);
            _events.Post(new PipelineWorkEvent(DateTimeOffset.UtcNow, () =>
            {
                if (sequence != _sequence || !IsActive)
                    return;
                var text = cells is null ? null
                    : DetailsRowFormatter.Format(cells, _settings.CurrentValue.ExplorerColumnHeaders, focus.PositionInSet, focus.SizeOfSet);
                text ??= announcement;
                if (!string.IsNullOrWhiteSpace(text))
                    _speech.Enqueue(new Utterance(text, priority));
            }));
        });
        return null;
    }
}
