using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Input;
using Vox.Core.Localization;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.AppModules.PowerPoint;

/// <summary>
/// PowerPoint. After focus changes and keys that can change the slide, the current slide is read
/// from the object model; when it is another slide, its number and title are said, and in a slide
/// show its text too. Commands move between the slide's shapes, read the speaker notes, and read
/// the whole slide.
/// </summary>
public sealed class PowerPointModule : AppModule
{
    // Long enough for PowerPoint to have changed slide after the key
    private static readonly TimeSpan SlideCheckDelay = TimeSpan.FromMilliseconds(150);

    private readonly IPowerPointSource _source;
    private readonly SpeechQueue _speech;
    private readonly IEventSink _events;
    private readonly ILogger _logger;
    private readonly TimeSpan _delay;

    // Pipeline thread only
    private long _sequence;
    private int _slideNumber;
    private bool _inShow;
    private int _shapeIndex;

    public PowerPointModule(IPowerPointSource source, SpeechQueue speech, IEventSink events,
        ILogger<PowerPointModule>? logger = null, TimeSpan? slideCheckDelay = null)
    {
        _source = source;
        _speech = speech;
        _events = events;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _delay = slideCheckDelay ?? SlideCheckDelay;
    }

    public override string Id => "powerpoint";
    public override string Name => "Microsoft PowerPoint";
    public override IReadOnlyCollection<string> ProcessNames { get; } = ["POWERPNT"];

    private IntPtr Window => Activation?.WindowHandle ?? IntPtr.Zero;

    public override void OnActivated(AppActivation activation)
    {
        base.OnActivated(activation);
        _slideNumber = 0;
        _inShow = false;
        _shapeIndex = 0;
    }

    /// <summary>Keys that can move to another slide: arrows, Page Up/Down, Home/End, Space, Enter, Backspace, N and P.</summary>
    public static bool MayChangeSlide(int vk) =>
        vk is >= 0x21 and <= 0x28 || vk is 0x20 or 0x0D or 0x08 or 0x4E or 0x50;

    public override void OnEvent(ScreenReaderEvent evt)
    {
        switch (evt)
        {
            case FocusChangedEvent:
                CheckSlide(TimeSpan.Zero);
                break;
            case RawKeyEvent { Key.IsKeyDown: true } key when MayChangeSlide(key.Key.VkCode):
                CheckSlide(_delay);
                break;
        }
    }

    private void CheckSlide(TimeSpan delay)
    {
        long sequence = ++_sequence;
        var window = Window;
        _ = Task.Run(async () =>
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay).ConfigureAwait(false);
            var slide = await _source.CurrentSlideAsync(window).ConfigureAwait(false);
            if (slide is not null)
                _events.Post(new PipelineWorkEvent(DateTimeOffset.UtcNow, () => SlideRead(sequence, slide)));
        });
    }

    private void SlideRead(long sequence, PowerPointSlide slide)
    {
        if (sequence != _sequence || !IsActive)
            return;
        bool changed = slide.Number != _slideNumber || slide.InSlideShow != _inShow;
        bool first = _slideNumber == 0;
        _slideNumber = slide.Number;
        _inShow = slide.InSlideShow;
        if (!changed)
            return;
        _shapeIndex = 0;
        if (slide.InSlideShow)
            _speech.Enqueue(new Utterance(PowerPointFormatter.SlideShowSlide(slide), SpeechPriority.Interrupt));
        // In the editor the focus announcement comes first; the slide is queued after it
        else if (!first)
            _speech.Enqueue(new Utterance(PowerPointFormatter.SlideChanged(slide), SpeechPriority.High));
    }

    public override bool TryHandle(NavigationCommand command)
    {
        var window = Window;
        switch (command)
        {
            case NavigationCommand.PowerPointNextShape:
            case NavigationCommand.PowerPointPrevShape:
            {
                bool forward = command == NavigationCommand.PowerPointNextShape;
                int from = _shapeIndex;
                Run(async () =>
                {
                    if (await _source.CurrentSlideAsync(window).ConfigureAwait(false) is not { } slide)
                        return Unavailable;
                    if (slide.Shapes.Count == 0)
                        return VoxStrings.Get("PowerPoint.NoShapes");
                    int index = forward ? from + 1 : from - 1;
                    if (index < 1 || index > slide.Shapes.Count)
                        return forward ? VoxStrings.Get("PowerPoint.NoNextShape") : VoxStrings.Get("PowerPoint.NoPreviousShape");
                    await _source.SelectShapeAsync(window, index).ConfigureAwait(false);
                    _events.Post(new PipelineWorkEvent(DateTimeOffset.UtcNow, () => _shapeIndex = index));
                    return PowerPointFormatter.Shape(slide.Shapes[index - 1], index, slide.Shapes.Count);
                });
                return true;
            }
            case NavigationCommand.PowerPointNotes:
                Run(async () => await _source.CurrentSlideAsync(window).ConfigureAwait(false) is { } slide
                    ? PowerPointFormatter.Notes(slide)
                    : Unavailable);
                return true;
            case NavigationCommand.PowerPointReadSlide:
                Run(async () => await _source.CurrentSlideAsync(window).ConfigureAwait(false) is { } slide
                    ? PowerPointFormatter.SlideShowSlide(slide)
                    : Unavailable);
                return true;
            default:
                return false;
        }
    }

    private static string Unavailable => VoxStrings.Get("PowerPoint.Unavailable");

    private void Run(Func<Task<string>> work) => _ = Task.Run(async () =>
    {
        try
        {
            _speech.Enqueue(new Utterance(await work().ConfigureAwait(false), SpeechPriority.Interrupt));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PowerPoint command failed");
        }
    });
}
