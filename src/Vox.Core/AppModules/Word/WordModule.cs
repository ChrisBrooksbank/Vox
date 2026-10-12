using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Localization;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.AppModules.Word;

/// <summary>
/// Microsoft Word. The document is read as any text control (caret, lines, spelling); the module
/// adds what needs Word's object model: quick navigation between headings, tables, lists,
/// comments, tracked changes and spelling errors (with the screen reader key and Alt, as the
/// document takes single letters as typing), comments and tracked changes said when the caret
/// enters them, the location report, and table cell navigation with Ctrl+Alt+arrows.
/// </summary>
public sealed class WordModule : AppModule
{
    private readonly IWordSource _source;
    private readonly SpeechQueue _speech;
    private readonly IEventSink _events;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly ILogger _logger;

    // Pipeline thread only
    private long _caretSequence;
    private WordCaretContext _caretContext = WordCaretContext.Empty;

    public WordModule(IWordSource source, SpeechQueue speech, IEventSink events, IOptionsMonitor<VoxSettings> settings,
        ILogger<WordModule>? logger = null)
    {
        _source = source;
        _speech = speech;
        _events = events;
        _settings = settings;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public override string Id => "word";
    public override string Name => "Microsoft Word";
    public override IReadOnlyCollection<string> ProcessNames { get; } = ["WINWORD"];

    private IntPtr Window => Activation?.WindowHandle ?? IntPtr.Zero;

    /// <summary>Each quick navigation command: what it moves to and which way.</summary>
    public static readonly IReadOnlyDictionary<NavigationCommand, (WordElementKind Kind, bool Forward)> QuickNavigation =
        new Dictionary<NavigationCommand, (WordElementKind, bool)>
        {
            [NavigationCommand.WordNextHeading] = (WordElementKind.Heading, true),
            [NavigationCommand.WordPrevHeading] = (WordElementKind.Heading, false),
            [NavigationCommand.WordNextTable] = (WordElementKind.Table, true),
            [NavigationCommand.WordPrevTable] = (WordElementKind.Table, false),
            [NavigationCommand.WordNextList] = (WordElementKind.List, true),
            [NavigationCommand.WordPrevList] = (WordElementKind.List, false),
            [NavigationCommand.WordNextComment] = (WordElementKind.Comment, true),
            [NavigationCommand.WordPrevComment] = (WordElementKind.Comment, false),
            [NavigationCommand.WordNextRevision] = (WordElementKind.Revision, true),
            [NavigationCommand.WordPrevRevision] = (WordElementKind.Revision, false),
            [NavigationCommand.WordNextSpellingError] = (WordElementKind.SpellingError, true),
            [NavigationCommand.WordPrevSpellingError] = (WordElementKind.SpellingError, false),
        };

    private static readonly IReadOnlyDictionary<NavigationCommand, WordTableMove> TableMoves =
        new Dictionary<NavigationCommand, WordTableMove>
        {
            [NavigationCommand.WordTableCellLeft] = WordTableMove.Left,
            [NavigationCommand.WordTableCellRight] = WordTableMove.Right,
            [NavigationCommand.WordTableCellUp] = WordTableMove.Up,
            [NavigationCommand.WordTableCellDown] = WordTableMove.Down,
        };

    public override void OnActivated(AppActivation activation)
    {
        base.OnActivated(activation);
        _caretContext = WordCaretContext.Empty;
    }

    public override bool TryHandle(NavigationCommand command)
    {
        var window = Window;
        if (QuickNavigation.TryGetValue(command, out var nav))
        {
            Run(async () =>
            {
                var result = await _source.MoveToAsync(window, nav.Kind, nav.Forward).ConfigureAwait(false);
                return result is null ? Unavailable
                    : result.Element is { } element ? WordAnnouncer.Element(element)
                    : WordAnnouncer.NotFound(nav.Kind, nav.Forward);
            });
            return true;
        }
        if (TableMoves.TryGetValue(command, out var move))
        {
            Run(async () =>
            {
                var result = await _source.MoveInTableAsync(window, move).ConfigureAwait(false);
                return result is null ? Unavailable
                    : !result.InTable ? VoxStrings.Get("Word.NotInTable")
                    : result.Cell is { } cell ? WordAnnouncer.Cell(cell, move)
                    : WordAnnouncer.TableEdge(move);
            });
            return true;
        }
        if (command == NavigationCommand.WordLocation)
        {
            Run(async () => await _source.LocationAsync(window).ConfigureAwait(false) is { } location
                ? WordAnnouncer.Location(location)
                : Unavailable);
            return true;
        }
        return false;
    }

    private static string Unavailable => VoxStrings.Get("Word.Unavailable");

    public override void OnEvent(ScreenReaderEvent evt)
    {
        if (evt is not CaretMovedEvent || !_settings.CurrentValue.WordReportCommentsAndRevisions)
            return;
        long sequence = ++_caretSequence;
        var window = Window;
        _ = Task.Run(async () =>
        {
            var context = await _source.CaretContextAsync(window).ConfigureAwait(false);
            if (context is not null)
                _events.Post(new PipelineWorkEvent(DateTimeOffset.UtcNow, () => CaretContextRead(sequence, context)));
        });
    }

    /// <summary>Says the comments and tracked changes the caret has moved into, after what the caret move says.</summary>
    private void CaretContextRead(long sequence, WordCaretContext context)
    {
        if (sequence != _caretSequence || !IsActive)
            return;
        var entered = WordAnnouncer.Entered(context, _caretContext);
        _caretContext = context;
        if (entered is not null)
            _speech.Enqueue(new Utterance(entered, SpeechPriority.Normal));
    }

    /// <summary>Runs a command's object-model work off the pipeline thread and says its result.</summary>
    private void Run(Func<Task<string>> work) => _ = Task.Run(async () =>
    {
        try
        {
            _speech.Enqueue(new Utterance(await work().ConfigureAwait(false), SpeechPriority.Interrupt));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Word command failed");
        }
    });
}
