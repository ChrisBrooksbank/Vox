using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vox.Core.Accessibility;
using Vox.Core.Lifecycle;
using Vox.Core.Magnifier;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;

namespace Vox.App;

/// <summary>
/// Connects the magnifier to the rest of Vox: starts it when its setting says, and feeds it the
/// focus, the caret, the browse cursor, the review cursor and the mouse pointer through
/// <see cref="MagnifierFeed"/>. Started after <see cref="ScreenReaderService"/>, so its pipeline
/// handlers run after that service's (the browse cursor has moved by then); the magnifier's
/// commands run with the other commands in <see cref="ScreenReaderService"/>. Not on the secure
/// screens or with the DisableMagnifier policy.
/// </summary>
public sealed class MagnifierService : IHostedService
{
    /// <summary>How often the mouse pointer is polled while the magnifier follows it.</summary>
    private static readonly TimeSpan PointerInterval = TimeSpan.FromMilliseconds(30);

    private readonly MagnifierController _controller;
    private readonly MagnifierFeed _feed;
    private readonly EventPipeline _pipeline;
    private readonly ReviewCommands _review;
    private readonly BrowseModeController _browse;
    private readonly NavigationManager _navigation;
    private readonly ILogger<MagnifierService> _logger;
    private readonly RunPolicy _runPolicy;
    private readonly CancellationTokenSource _stop = new();
    private Task? _pointerLoop;
    private bool _started;

    public MagnifierService(MagnifierController controller, EventPipeline pipeline, ReviewCommands review,
        BrowseModeController browse, NavigationManager navigation, BrowseDocumentTracker tracker,
        ILogger<MagnifierService> logger, RunPolicy? runPolicy = null)
    {
        _controller = controller;
        _pipeline = pipeline;
        _review = review;
        _browse = browse;
        _navigation = navigation;
        _logger = logger;
        _runPolicy = runPolicy ?? RunPolicy.Normal;
        _feed = new MagnifierFeed(controller, tracker.ElementBoundsAsync, Win32Caret.GetCaretBounds, logger);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_runPolicy.AllowMagnifier)
            return Task.CompletedTask;
        _started = true;

        _pipeline.FocusChangedProcessed += OnFocusChanged;
        _pipeline.CaretMovedProcessed += OnCaretEvent;
        _pipeline.TextEditedProcessed += OnCaretEvent;
        _pipeline.RawKeyReceived += OnRawKey;
        _pipeline.NavigationCommandReceived += OnBrowseEvent;
        _pipeline.DocumentChangedProcessed += OnBrowseEvent;
        _pipeline.SubtreeChangedProcessed += OnBrowseEvent;
        _pipeline.FocusInDocumentProcessed += OnBrowseEvent;
        _pipeline.ElementsListClosedProcessed += OnBrowseEvent;
        _pipeline.FindPromptClosedProcessed += OnBrowseEvent;
        _pipeline.VoxDialogClosedProcessed += OnBrowseEvent;
        _pipeline.BrailleRouteProcessed += OnBrowseEvent;
        _navigation.ModeChanged += OnBrowseEvent;
        _review.PositionShown += _feed.ReviewMoved;
        _controller.Start();
        _pointerLoop = Task.Run(PollPointerAsync);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started)
            return;
        _pipeline.FocusChangedProcessed -= OnFocusChanged;
        _pipeline.CaretMovedProcessed -= OnCaretEvent;
        _pipeline.TextEditedProcessed -= OnCaretEvent;
        _pipeline.RawKeyReceived -= OnRawKey;
        _pipeline.NavigationCommandReceived -= OnBrowseEvent;
        _pipeline.DocumentChangedProcessed -= OnBrowseEvent;
        _pipeline.SubtreeChangedProcessed -= OnBrowseEvent;
        _pipeline.FocusInDocumentProcessed -= OnBrowseEvent;
        _pipeline.ElementsListClosedProcessed -= OnBrowseEvent;
        _pipeline.FindPromptClosedProcessed -= OnBrowseEvent;
        _pipeline.VoxDialogClosedProcessed -= OnBrowseEvent;
        _pipeline.BrailleRouteProcessed -= OnBrowseEvent;
        _navigation.ModeChanged -= OnBrowseEvent;
        _review.PositionShown -= _feed.ReviewMoved;
        _stop.Cancel();
        if (_pointerLoop is not null)
        {
            try { await _pointerLoop.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _stop.Dispose();
        // The screen goes back as it was
        _controller.Dispose();
    }

    private void OnFocusChanged(object? sender, FocusChangedEvent e)
    {
        _feed.FocusChanged(e);
        RefreshBrowse();
    }

    private void OnCaretEvent<T>(object? sender, T e) => _feed.CaretMoved();

    private void OnRawKey(object? sender, RawKeyEvent e)
    {
        if (e.Key.IsKeyDown)
            _ = _feed.KeyTypedAsync();
    }

    // Raised on the pipeline thread (mode and document changes come from its handlers)
    private void OnBrowseEvent<T>(object? sender, T e) => RefreshBrowse();

    private void RefreshBrowse() => _feed.BrowseUpdated(_browse.BrowsePosition()?.Cursor.CurrentNode);

    private async Task PollPointerAsync()
    {
        using var timer = new PeriodicTimer(PointerInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
            {
                if (_controller.Follows(MagnifierTrackingSource.Mouse))
                    _feed.PointerPolled(PhysicalPointer.GetPosition());
                else
                    _feed.PointerPaused();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The magnifier stopped following the mouse");
        }
    }
}
