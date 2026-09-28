using System.ComponentModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.App;

/// <summary>
/// Main hosted service for the Vox screen reader.
/// Initializes all subsystems, wires pipeline events to their handlers and manages application lifecycle.
/// Navigation logic lives in <see cref="BrowseModeController"/>; this class only connects components.
/// </summary>
public sealed class ScreenReaderService : IHostedService
{
    private readonly ISpeechEngine _speechEngine;
    private readonly SpeechQueue _speechQueue;
    private readonly EventPipeline _eventPipeline;
    private readonly IKeyboardHook _keyboardHook;
    private readonly KeyInputDispatcher _keyInputDispatcher;
    private readonly UIAProvider _uiaProvider;
    private readonly UIAEventSubscriber _uiaEventSubscriber;
    private readonly BrowseDocumentTracker _documentTracker;
    private readonly NavigationManager _navigationManager;
    private readonly BrowseModeController _browseModeController;
    private readonly SayAllController _sayAllController;
    private readonly IAudioCuePlayer _audioCuePlayer;
    private readonly FirstRunWizard _firstRunWizard;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly ILogger<ScreenReaderService> _logger;

    private IDisposable? _settingsSubscription;

    public ScreenReaderService(
        ISpeechEngine speechEngine,
        SpeechQueue speechQueue,
        EventPipeline eventPipeline,
        IKeyboardHook keyboardHook,
        KeyInputDispatcher keyInputDispatcher,
        UIAProvider uiaProvider,
        UIAEventSubscriber uiaEventSubscriber,
        BrowseDocumentTracker documentTracker,
        NavigationManager navigationManager,
        BrowseModeController browseModeController,
        SayAllController sayAllController,
        IAudioCuePlayer audioCuePlayer,
        FirstRunWizard firstRunWizard,
        IOptionsMonitor<VoxSettings> settings,
        ILogger<ScreenReaderService> logger)
    {
        _speechEngine = speechEngine;
        _speechQueue = speechQueue;
        _eventPipeline = eventPipeline;
        _keyboardHook = keyboardHook;
        _keyInputDispatcher = keyInputDispatcher;
        _uiaProvider = uiaProvider;
        _uiaEventSubscriber = uiaEventSubscriber;
        _documentTracker = documentTracker;
        _navigationManager = navigationManager;
        _browseModeController = browseModeController;
        _sayAllController = sayAllController;
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

        // Subscribe to UIA events (focus, live regions, notifications)
        await _uiaEventSubscriber.SubscribeAsync();

        // Apply settings now and whenever they change (wizard, settings.json edits)
        ApplySettings(_settings.CurrentValue);
        _settingsSubscription = _settings.OnChange((s, _) => ApplySettings(s));

        // Install the low-level keyboard hook (needed before wizard for key input)
        bool hookInstalled = TryInstallKeyboardHook();

        // Check if first run wizard needs to run (before starting normal pipeline)
        if (hookInstalled && !_settings.CurrentValue.FirstRunCompleted)
        {
            _logger.LogInformation("First run not completed — starting wizard");
            await _firstRunWizard.RunAsync(cancellationToken);
        }

        // Wire pipeline events (all raised on the pipeline thread)
        _eventPipeline.RawKeyReceived += OnRawKeyReceived;
        _eventPipeline.NavigationCommandReceived += OnNavigationCommandReceived;
        _eventPipeline.FocusChangedProcessed += OnFocusChangedProcessed;
        _eventPipeline.StructureChangedProcessed += OnStructureChangedProcessed;
        _eventPipeline.DocumentChangedProcessed += OnDocumentChangedProcessed;
        _eventPipeline.SubtreeChangedProcessed += OnSubtreeChangedProcessed;
        _eventPipeline.ElementsListClosedProcessed += OnElementsListClosedProcessed;

        // Keep key resolution in sync with the browse/focus mode and document focus
        _navigationManager.ModeChanged += OnModeChanged;
        _browseModeController.DocumentActiveChanged += OnDocumentActiveChanged;
        _keyInputDispatcher.SetMode(_navigationManager.CurrentMode);
        _keyInputDispatcher.SetDocumentActive(_browseModeController.IsDocumentActive);

        // Start key input dispatcher (subscribes to keyboard hook, installs key suppression)
        _keyInputDispatcher.Start();

        // Pick up a browser that already has focus
        _ = _documentTracker.OnFocusChangedAsync();

        // Announce startup
        _speechQueue.Enqueue(new Utterance(
            hookInstalled
                ? "Vox screen reader ready"
                : "Vox screen reader ready, but keyboard commands are unavailable. Try running Vox as administrator.",
            SpeechPriority.Normal));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Vox Screen Reader stopping");

        // Stop key input first so no new commands arrive
        _keyInputDispatcher.Stop();
        _keyboardHook.Uninstall();

        // Unsubscribe event handlers
        _eventPipeline.RawKeyReceived -= OnRawKeyReceived;
        _eventPipeline.NavigationCommandReceived -= OnNavigationCommandReceived;
        _eventPipeline.FocusChangedProcessed -= OnFocusChangedProcessed;
        _eventPipeline.StructureChangedProcessed -= OnStructureChangedProcessed;
        _eventPipeline.DocumentChangedProcessed -= OnDocumentChangedProcessed;
        _eventPipeline.SubtreeChangedProcessed -= OnSubtreeChangedProcessed;
        _eventPipeline.ElementsListClosedProcessed -= OnElementsListClosedProcessed;
        _navigationManager.ModeChanged -= OnModeChanged;
        _browseModeController.DocumentActiveChanged -= OnDocumentActiveChanged;
        _settingsSubscription?.Dispose();

        // Stop Say All if running
        _sayAllController.Cancel();

        // Stop pending buffer updates, then release UIA (both run on the STA thread in order)
        _documentTracker.Dispose();
        _uiaEventSubscriber.Dispose();
        _uiaProvider.Dispose();

        _speechEngine.Cancel();

        await Task.CompletedTask;
    }

    private bool TryInstallKeyboardHook()
    {
        try
        {
            _keyboardHook.Install();
            return true;
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Keyboard hook could not be installed; keyboard commands are disabled");
            return false;
        }
    }

    private void ApplySettings(VoxSettings settings)
    {
        _speechEngine.SetRate(settings.SpeechRateWpm);
        if (!string.IsNullOrEmpty(settings.VoiceName))
            _speechEngine.SetVoice(settings.VoiceName);
        _audioCuePlayer.IsEnabled = settings.AudioCuesEnabled;
        _keyboardHook.ScreenReaderModifier = settings.ModifierKey;
    }

    private void OnRawKeyReceived(object? sender, RawKeyEvent e) =>
        _browseModeController.HandleRawKey(e);

    private void OnNavigationCommandReceived(object? sender, NavigationCommandEvent e) =>
        _browseModeController.HandleCommand(e.Command);

    private void OnFocusChangedProcessed(object? sender, FocusChangedEvent e)
    {
        _browseModeController.HandleFocusChanged(e);
        _ = _documentTracker.OnFocusChangedAsync();
    }

    private void OnStructureChangedProcessed(object? sender, StructureChangedEvent e) =>
        _documentTracker.OnStructureChanged(e.RuntimeId);

    private void OnDocumentChangedProcessed(object? sender, DocumentChangedEvent e) =>
        _browseModeController.HandleDocumentChanged(e);

    private void OnSubtreeChangedProcessed(object? sender, SubtreeChangedEvent e) =>
        _browseModeController.HandleSubtreeChanged(e);

    private void OnElementsListClosedProcessed(object? sender, ElementsListClosedEvent e) =>
        _browseModeController.HandleElementsListClosed(e);

    private void OnModeChanged(object? sender, InteractionMode mode) =>
        _keyInputDispatcher.SetMode(mode);

    private void OnDocumentActiveChanged(object? sender, bool active) =>
        _keyInputDispatcher.SetDocumentActive(active);
}
