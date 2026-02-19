using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.App;

/// <summary>
/// Main hosted service for the Vox screen reader.
/// Initializes all subsystems and manages application lifecycle.
/// </summary>
public sealed class ScreenReaderService : IHostedService
{
    private readonly ISpeechEngine _speechEngine;
    private readonly SpeechQueue _speechQueue;
    private readonly EventPipeline _eventPipeline;
    private readonly IKeyboardHook _keyboardHook;
    private readonly KeyInputDispatcher _keyInputDispatcher;
    private readonly TypingEchoHandler _typingEchoHandler;
    private readonly UIAProvider _uiaProvider;
    private readonly UIAEventSubscriber _uiaEventSubscriber;
    private readonly NavigationManager _navigationManager;
    private readonly QuickNavHandler _quickNavHandler;
    private readonly SayAllController _sayAllController;
    private readonly AnnouncementBuilder _announcementBuilder;
    private readonly Vox.Core.Audio.IAudioCuePlayer _audioCuePlayer;
    private readonly FirstRunWizard _firstRunWizard;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly ILogger<ScreenReaderService> _logger;

    public ScreenReaderService(
        ISpeechEngine speechEngine,
        SpeechQueue speechQueue,
        EventPipeline eventPipeline,
        IKeyboardHook keyboardHook,
        KeyInputDispatcher keyInputDispatcher,
        TypingEchoHandler typingEchoHandler,
        UIAProvider uiaProvider,
        UIAEventSubscriber uiaEventSubscriber,
        NavigationManager navigationManager,
        QuickNavHandler quickNavHandler,
        SayAllController sayAllController,
        AnnouncementBuilder announcementBuilder,
        Vox.Core.Audio.IAudioCuePlayer audioCuePlayer,
        FirstRunWizard firstRunWizard,
        IOptionsMonitor<VoxSettings> settings,
        ILogger<ScreenReaderService> logger)
    {
        _speechEngine = speechEngine;
        _speechQueue = speechQueue;
        _eventPipeline = eventPipeline;
        _keyboardHook = keyboardHook;
        _keyInputDispatcher = keyInputDispatcher;
        _typingEchoHandler = typingEchoHandler;
        _uiaProvider = uiaProvider;
        _uiaEventSubscriber = uiaEventSubscriber;
        _navigationManager = navigationManager;
        _quickNavHandler = quickNavHandler;
        _sayAllController = sayAllController;
        _announcementBuilder = announcementBuilder;
        _audioCuePlayer = audioCuePlayer;
        _firstRunWizard = firstRunWizard;
        _settings = settings;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Vox Screen Reader starting");

        // Initialize UIA on the dedicated STA thread
        await _uiaProvider.InitializeAsync();

        // Subscribe to UIA events (focus, structure, live regions, etc.)
        await _uiaEventSubscriber.SubscribeAsync();

        // Install the low-level keyboard hook (needed before wizard for key input)
        _keyboardHook.Install();

        // Check if first run wizard needs to run (before starting normal pipeline)
        if (!_settings.CurrentValue.FirstRunCompleted)
        {
            _logger.LogInformation("First run not completed — starting wizard");
            await _firstRunWizard.RunAsync(cancellationToken);
        }

        // Start typing echo handler (subscribes to pipeline RawKeyEvents)
        _eventPipeline.RawKeyReceived += OnRawKeyReceived;

        // Wire navigation components to pipeline events
        _eventPipeline.NavigationCommandReceived += OnNavigationCommandReceived;
        _eventPipeline.FocusChangedProcessed += OnFocusChangedProcessed;

        // Start key input dispatcher (subscribes to keyboard hook)
        _keyInputDispatcher.Start();

        // Announce startup
        _speechQueue.Enqueue(new Utterance("Vox screen reader ready", SpeechPriority.Normal));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Vox Screen Reader stopping");

        // Uninstall keyboard hook first to stop new events
        _keyboardHook.Uninstall();

        // Stop key input dispatcher
        _keyInputDispatcher.Stop();

        // Unsubscribe event handlers
        _eventPipeline.RawKeyReceived -= OnRawKeyReceived;
        _eventPipeline.NavigationCommandReceived -= OnNavigationCommandReceived;
        _eventPipeline.FocusChangedProcessed -= OnFocusChangedProcessed;

        // Stop Say All if running
        _sayAllController.Cancel();

        // Dispose UIA event subscriber (unsubscribes from all UIA events)
        _uiaEventSubscriber.Dispose();

        // Dispose UIA provider (releases COM objects on STA thread)
        _uiaProvider.Dispose();

        _speechEngine.Cancel();

        await Task.CompletedTask;
    }

    private void OnRawKeyReceived(object? sender, RawKeyEvent e)
    {
        _typingEchoHandler.HandleKeyEvent(e);
    }

    private void OnNavigationCommandReceived(object? sender, NavigationCommandEvent e)
    {
        var command = e.Command;

        // StopSpeech: cancel Say All and interrupt TTS
        if (command == NavigationCommand.StopSpeech)
        {
            _sayAllController.Cancel();
            _speechEngine.Cancel();
            return;
        }

        // SayAll: start continuous reading from current document position
        if (command == NavigationCommand.SayAll)
        {
            var doc = _quickNavHandler.CurrentDocument;
            if (doc is not null)
            {
                var cursor = new Vox.Core.Buffer.VBufferCursor(doc, _audioCuePlayer);
                _sayAllController.Start(cursor);
            }
            else
            {
                _logger.LogDebug("SayAll requested but no document is loaded");
            }
            return;
        }

        // Let NavigationManager decide if the command should be handled or blocked
        bool handled = _navigationManager.HandleCommand(command, _quickNavHandler.CurrentNode);
        if (handled) return;

        // In Browse mode, pass quick-nav commands to QuickNavHandler
        if (_navigationManager.CurrentMode == InteractionMode.Browse)
        {
            var node = _quickNavHandler.Handle(command);
            if (node is not null)
            {
                var text = _announcementBuilder.Build(node, Vox.Core.Configuration.VerbosityLevel.Beginner);
                if (!string.IsNullOrWhiteSpace(text))
                    _speechQueue.Enqueue(new Utterance(text, SpeechPriority.High));
            }
        }
    }

    private void OnFocusChangedProcessed(object? sender, FocusChangedEvent e)
    {
        _navigationManager.HandleFocusChanged(e);
    }
}
