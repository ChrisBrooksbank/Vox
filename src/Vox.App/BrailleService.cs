using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vox.Core.Accessibility;
using Vox.Core.Braille;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.App;

/// <summary>
/// Connects braille to the rest of Vox: finds the display (<see cref="BrailleDisplayManager"/>),
/// feeds <see cref="BrailleController"/> the focus, caret, browse cursor and review cursor, shows
/// what Vox says as messages, and runs the braille commands and routing keys. Started after
/// <see cref="ScreenReaderService"/>, so its pipeline handlers run after that service's (the browse
/// cursor has moved by then). Not in secure mode.
/// </summary>
public sealed class BrailleService : IHostedService
{
    private readonly BrailleDisplayManager _displays;
    private readonly BrailleController _controller;
    private readonly BrailleFeed _feed;
    private readonly EventPipeline _pipeline;
    private readonly SpeechQueue _speechQueue;
    private readonly FocusedTextMonitor _focusedText;
    private readonly ReviewCommands _review;
    private readonly BrowseModeController _browse;
    private readonly NavigationManager _navigation;
    private readonly RunPolicy _runPolicy;
    private bool _started;

    public BrailleService(BrailleDisplayManager displays, BrailleController controller, UIAFocusAncestry ancestry,
        EventPipeline pipeline, SpeechQueue speechQueue, FocusedTextMonitor focusedText, ReviewCommands review,
        BrowseModeController browse, NavigationManager navigation, ILogger<BrailleService> logger, RunPolicy? runPolicy = null)
    {
        _displays = displays;
        _controller = controller;
        _pipeline = pipeline;
        _speechQueue = speechQueue;
        _focusedText = focusedText;
        _review = review;
        _browse = browse;
        _navigation = navigation;
        _runPolicy = runPolicy ?? RunPolicy.Normal;
        _feed = new BrailleFeed(controller, ancestry.GetAsync, logger);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Braille on the sign-in and UAC screens is out of scope for now
        if (_runPolicy.IsSecure)
            return Task.CompletedTask;
        _started = true;

        _pipeline.FocusChangedProcessed += OnFocusChanged;
        _pipeline.NavigationCommandReceived += OnCommand;
        _pipeline.DocumentChangedProcessed += OnBrowseEvent;
        _pipeline.SubtreeChangedProcessed += OnBrowseEvent;
        _pipeline.FocusInDocumentProcessed += OnBrowseEvent;
        _pipeline.ElementsListClosedProcessed += OnBrowseEvent;
        _pipeline.FindPromptClosedProcessed += OnBrowseEvent;
        _pipeline.VoxDialogClosedProcessed += OnBrowseEvent;
        _pipeline.BrailleRouteProcessed += OnBrailleRoute;
        _navigation.ModeChanged += OnBrowseEvent;
        _browse.DocumentActiveChanged += OnBrowseEvent;
        _focusedText.DocumentRead = _feed.TextRead;
        _review.PositionShown = _feed.ReviewMoved;
        _speechQueue.UtteranceQueued += OnUtteranceQueued;
        _controller.TetherChanged += OnTetherChanged;
        _displays.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started)
            return Task.CompletedTask;
        _pipeline.FocusChangedProcessed -= OnFocusChanged;
        _pipeline.NavigationCommandReceived -= OnCommand;
        _pipeline.DocumentChangedProcessed -= OnBrowseEvent;
        _pipeline.SubtreeChangedProcessed -= OnBrowseEvent;
        _pipeline.FocusInDocumentProcessed -= OnBrowseEvent;
        _pipeline.ElementsListClosedProcessed -= OnBrowseEvent;
        _pipeline.FindPromptClosedProcessed -= OnBrowseEvent;
        _pipeline.VoxDialogClosedProcessed -= OnBrowseEvent;
        _pipeline.BrailleRouteProcessed -= OnBrailleRoute;
        _navigation.ModeChanged -= OnBrowseEvent;
        _browse.DocumentActiveChanged -= OnBrowseEvent;
        _focusedText.DocumentRead = null;
        _review.PositionShown = null;
        _speechQueue.UtteranceQueued -= OnUtteranceQueued;
        _controller.TetherChanged -= OnTetherChanged;
        // Leaves BRLTTY's tty mode and closes the HID device
        _controller.Dispose();
        _displays.Dispose();
        return Task.CompletedTask;
    }

    private void OnFocusChanged(object? sender, FocusChangedEvent e)
    {
        _feed.FocusChanged(e);
        RefreshBrowse();
    }

    private void OnCommand(object? sender, NavigationCommandEvent e)
    {
        switch (e.Command)
        {
            case NavigationCommand.BraillePanLeft: _controller.PanLeft(); return;
            case NavigationCommand.BraillePanRight: _controller.PanRight(); return;
            case NavigationCommand.CycleBrailleTether: _controller.CycleTether(); return;
        }
        RefreshBrowse();
    }

    private void OnBrailleRoute(object? sender, BrailleRouteEvent e)
    {
        _browse.RouteTo(e.Offset, e.Activate);
        RefreshBrowse();
    }

    // Raised on the pipeline thread (mode and document changes come from its handlers)
    private void OnBrowseEvent<T>(object? sender, T e) => RefreshBrowse();

    private void RefreshBrowse() => _feed.BrowseUpdated(_browse.BrowsePosition());

    private void OnUtteranceQueued(object? sender, Utterance utterance) => _controller.OfferMessage(utterance.Text);

    private void OnTetherChanged(object? sender, BrailleTether tether) =>
        _speechQueue.Enqueue(new Utterance(BrailleController.Describe(tether), SpeechPriority.Interrupt));
}
