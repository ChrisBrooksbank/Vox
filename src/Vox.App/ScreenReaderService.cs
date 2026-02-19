using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vox.Core.Accessibility;
using Vox.Core.Input;
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
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Vox Screen Reader starting");

        // Initialize UIA on the dedicated STA thread
        await _uiaProvider.InitializeAsync();

        // Subscribe to UIA events (focus, structure, live regions, etc.)
        await _uiaEventSubscriber.SubscribeAsync();

        // Start typing echo handler (subscribes to pipeline RawKeyEvents)
        _eventPipeline.RawKeyReceived += OnRawKeyReceived;

        // Start key input dispatcher (subscribes to keyboard hook)
        _keyInputDispatcher.Start();

        // Install the low-level keyboard hook
        _keyboardHook.Install();

        // Announce startup
        _speechQueue.Enqueue(new Utterance("Vox screen reader started", SpeechPriority.Normal));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Vox Screen Reader stopping");

        // Uninstall keyboard hook first to stop new events
        _keyboardHook.Uninstall();

        // Stop key input dispatcher
        _keyInputDispatcher.Stop();

        // Unsubscribe typing echo handler
        _eventPipeline.RawKeyReceived -= OnRawKeyReceived;

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
}
