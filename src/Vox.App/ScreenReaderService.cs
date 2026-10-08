using System.ComponentModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
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
    private readonly UIAWatchdog _uiaWatchdog;
    private readonly UIARecovery _uiaRecovery;
    // Constructed to subscribe to UIA call timeouts ("<app> not responding")
    private readonly NotRespondingReporter _notRespondingReporter;
    private readonly FocusedTextMonitor _focusedTextMonitor;
    private readonly TerminalMonitor _terminalMonitor;
    private readonly ForegroundWindowMonitor _foregroundWindowMonitor;
    private readonly DialogReader _dialogReader;
    private readonly ProgressReporter _progressReporter;
    private readonly MenuTracker _menuTracker;
    private readonly WhereAmICommands _whereAmI;
    private readonly ObjectNavigationCommands _objectNavigation;
    private readonly ReviewCommands _review;
    private readonly MouseTracker _mouseTracker;
    private readonly SpeechEngineRegistry _speechEngines;
    private readonly SettingsRing _settingsRing;
    private readonly SpeechHistoryCommands _speechHistoryCommands;
    private readonly SleepMode _sleepMode;
    private readonly MouseCommands _mouseCommands;
    private readonly RunPolicy _runPolicy;
    private readonly SettingsManager _settingsManager;
    private readonly IStartupRegistration _startupRegistration;
    private readonly DuckingController _duckingController;
    private readonly SpeechViewer _speechViewer;
    private readonly NavigationManager _navigationManager;
    private readonly BrowseModeController _browseModeController;
    private readonly SayAllController _sayAllController;
    private readonly IAudioCuePlayer _audioCuePlayer;
    private readonly FirstRunWizard _firstRunWizard;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ScreenReaderService> _logger;
    private int _setupRunning;

    private IDisposable? _settingsSubscription;
    private VoxSettings? _appliedSettings;
    private readonly object _settingsLock = new();

    // Tasks spawned fire-and-forget from event handlers (which can't be awaited directly) that
    // touch UIA objects. StopAsync drains these before disposing _documentTracker,
    // _uiaEventSubscriber and _uiaProvider, so a handler still in flight during shutdown can't
    // race the dispose calls and use an already-released UIA object.
    private readonly List<Task> _backgroundTasks = new();
    private readonly object _backgroundTasksLock = new();

    private void TrackBackground(Task task)
    {
        lock (_backgroundTasksLock) { _backgroundTasks.Add(task); }
        task.ContinueWith(t =>
        {
            lock (_backgroundTasksLock) { _backgroundTasks.Remove(t); }
        }, TaskScheduler.Default);
    }

    public ScreenReaderService(
        ISpeechEngine speechEngine,
        SpeechQueue speechQueue,
        EventPipeline eventPipeline,
        IKeyboardHook keyboardHook,
        KeyInputDispatcher keyInputDispatcher,
        UIAProvider uiaProvider,
        UIAEventSubscriber uiaEventSubscriber,
        BrowseDocumentTracker documentTracker,
        UIAWatchdog uiaWatchdog,
        UIARecovery uiaRecovery,
        NotRespondingReporter notRespondingReporter,
        FocusedTextMonitor focusedTextMonitor,
        TerminalMonitor terminalMonitor,
        ForegroundWindowMonitor foregroundWindowMonitor,
        DialogReader dialogReader,
        ProgressReporter progressReporter,
        MenuTracker menuTracker,
        WhereAmICommands whereAmI,
        NavigationManager navigationManager,
        BrowseModeController browseModeController,
        SayAllController sayAllController,
        IAudioCuePlayer audioCuePlayer,
        FirstRunWizard firstRunWizard,
        IOptionsMonitor<VoxSettings> settings,
        IHostApplicationLifetime lifetime,
        ILogger<ScreenReaderService> logger,
        SettingsManager settingsManager,
        IStartupRegistration startupRegistration,
        DuckingController duckingController,
        SpeechViewer speechViewer,
        ObjectNavigationCommands objectNavigation,
        ReviewCommands review,
        MouseTracker mouseTracker,
        SpeechEngineRegistry speechEngines,
        SettingsRing settingsRing,
        SpeechHistoryCommands speechHistoryCommands,
        SleepMode sleepMode,
        MouseCommands mouseCommands,
        RunPolicy? runPolicy = null)
    {
        _review = review;
        _mouseTracker = mouseTracker;
        _speechEngines = speechEngines;
        _settingsRing = settingsRing;
        _speechHistoryCommands = speechHistoryCommands;
        _sleepMode = sleepMode;
        _mouseCommands = mouseCommands;
        _objectNavigation = objectNavigation;
        _speechViewer = speechViewer;
        _duckingController = duckingController;
        _startupRegistration = startupRegistration;
        _settingsManager = settingsManager;
        _runPolicy = runPolicy ?? RunPolicy.Normal;
        _lifetime = lifetime;
        _speechEngine = speechEngine;
        _speechQueue = speechQueue;
        _eventPipeline = eventPipeline;
        _keyboardHook = keyboardHook;
        _keyInputDispatcher = keyInputDispatcher;
        _uiaProvider = uiaProvider;
        _uiaEventSubscriber = uiaEventSubscriber;
        _documentTracker = documentTracker;
        _uiaWatchdog = uiaWatchdog;
        _uiaRecovery = uiaRecovery;
        _notRespondingReporter = notRespondingReporter;
        _focusedTextMonitor = focusedTextMonitor;
        _terminalMonitor = terminalMonitor;
        _foregroundWindowMonitor = foregroundWindowMonitor;
        _dialogReader = dialogReader;
        _progressReporter = progressReporter;
        _menuTracker = menuTracker;
        _whereAmI = whereAmI;
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

        // Apply settings now and whenever they change (wizard, settings.json edits)
        ApplySettings(_settings.CurrentValue);
        _settingsSubscription = _settings.OnChange((s, _) => ApplySettings(s));

        // Install the low-level keyboard hook (needed before wizard for key input)
        bool hookInstalled = TryInstallKeyboardHook();

        // Run the first-run wizard before subscribing to UIA events: focus announcements are
        // Interrupt speech and would cut off the wizard's prompts
        if (hookInstalled && _runPolicy.AllowSetupWizard && !_settings.CurrentValue.FirstRunCompleted)
        {
            _logger.LogInformation("First run not completed — starting wizard");
            await _firstRunWizard.RunAsync(cancellationToken);
        }

        // Subscribe to UIA events (focus, live regions, notifications)
        await _uiaEventSubscriber.SubscribeAsync();

        // Replace the UIA thread if an unresponsive app leaves it stuck in a call; _uiaRecovery
        // then re-creates the automation object, subscriptions and document on the new thread
        _uiaWatchdog.Start();

        // Speak what is under the mouse pointer while mouse tracking is on
        _mouseTracker.Start();

        // Wire pipeline events (all raised on the pipeline thread)
        _eventPipeline.RawKeyReceived += OnRawKeyReceived;
        _eventPipeline.NavigationCommandReceived += OnNavigationCommandReceived;
        _eventPipeline.FocusChangedProcessed += OnFocusChangedProcessed;
        _eventPipeline.StructureChangedProcessed += OnStructureChangedProcessed;
        _eventPipeline.DocumentChangedProcessed += OnDocumentChangedProcessed;
        _eventPipeline.SubtreeChangedProcessed += OnSubtreeChangedProcessed;
        _eventPipeline.FocusInDocumentProcessed += OnFocusInDocumentProcessed;
        _eventPipeline.ElementsListClosedProcessed += OnElementsListClosedProcessed;
        _eventPipeline.FindPromptClosedProcessed += OnFindPromptClosedProcessed;
        _eventPipeline.PropertyChangedProcessed += OnPropertyChangedProcessed;
        _eventPipeline.ElementSelectedProcessed += OnElementSelectedProcessed;
        _eventPipeline.CaretMovedProcessed += OnCaretMovedProcessed;
        _eventPipeline.TextEditedProcessed += OnTextEditedProcessed;
        _eventPipeline.ForegroundWindowChangedProcessed += OnForegroundWindowChangedProcessed;
        _eventPipeline.ProgressChangedProcessed += OnProgressChangedProcessed;
        _eventPipeline.MenuEventProcessed += OnMenuEventProcessed;
        _eventPipeline.FocusAnnouncementFilter = _browseModeController.ShouldAnnounceFocus;
        _eventPipeline.FocusContextProvider = FocusContext;
        _eventPipeline.ErrorMessageProvider = _browseModeController.ErrorMessageFor;
        // In browse mode the review cursor reviews the virtual buffer
        _review.BrowseTether = _browseModeController.ReviewTether;

        // Keep key resolution in sync with the browse/focus mode and document focus
        _navigationManager.ModeChanged += OnModeChanged;
        _browseModeController.DocumentActiveChanged += OnDocumentActiveChanged;
        _browseModeController.EscapeGoesToPageChanged += OnEscapeGoesToPageChanged;
        _browseModeController.QuitRequested += OnQuitRequested;
        _browseModeController.SetupRequested += OnSetupRequested;
        _keyInputDispatcher.SetMode(_navigationManager.CurrentMode);
        _keyInputDispatcher.SetDocumentActive(_browseModeController.IsDocumentActive);

        // Sleep mode: nothing is said, and keys go to the application, while it has focus
        _speechQueue.IsMuted = _sleepMode.IsAsleepNow;
        _keyInputDispatcher.IsAsleep = () => _sleepMode.IsAsleepCached;

        // Start key input dispatcher (subscribes to keyboard hook, installs key suppression)
        _keyInputDispatcher.Start();

        // Pick up a browser that already has focus
        TrackBackground(_documentTracker.OnFocusChangedAsync());

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

        // No thread replacement or recovery while shutting down
        _uiaWatchdog.Dispose();
        _uiaRecovery.Dispose();
        _notRespondingReporter.Dispose();

        // Unsubscribe event handlers
        _eventPipeline.RawKeyReceived -= OnRawKeyReceived;
        _eventPipeline.NavigationCommandReceived -= OnNavigationCommandReceived;
        _eventPipeline.FocusChangedProcessed -= OnFocusChangedProcessed;
        _eventPipeline.StructureChangedProcessed -= OnStructureChangedProcessed;
        _eventPipeline.DocumentChangedProcessed -= OnDocumentChangedProcessed;
        _eventPipeline.SubtreeChangedProcessed -= OnSubtreeChangedProcessed;
        _eventPipeline.FocusInDocumentProcessed -= OnFocusInDocumentProcessed;
        _eventPipeline.ElementsListClosedProcessed -= OnElementsListClosedProcessed;
        _eventPipeline.FindPromptClosedProcessed -= OnFindPromptClosedProcessed;
        _eventPipeline.PropertyChangedProcessed -= OnPropertyChangedProcessed;
        _eventPipeline.ElementSelectedProcessed -= OnElementSelectedProcessed;
        _eventPipeline.CaretMovedProcessed -= OnCaretMovedProcessed;
        _eventPipeline.TextEditedProcessed -= OnTextEditedProcessed;
        _eventPipeline.ForegroundWindowChangedProcessed -= OnForegroundWindowChangedProcessed;
        _eventPipeline.ProgressChangedProcessed -= OnProgressChangedProcessed;
        _eventPipeline.MenuEventProcessed -= OnMenuEventProcessed;
        _navigationManager.ModeChanged -= OnModeChanged;
        _browseModeController.DocumentActiveChanged -= OnDocumentActiveChanged;
        _browseModeController.EscapeGoesToPageChanged -= OnEscapeGoesToPageChanged;
        _browseModeController.QuitRequested -= OnQuitRequested;
        _browseModeController.SetupRequested -= OnSetupRequested;
        _eventPipeline.FocusAnnouncementFilter = null;
        _eventPipeline.FocusContextProvider = null;
        _review.BrowseTether = null;
        _speechQueue.IsMuted = null;
        _keyInputDispatcher.IsAsleep = null;
        _settingsSubscription?.Dispose();

        // Stop Say All if running
        _sayAllController.Cancel();

        // No more FocusChangedProcessed handlers can fire past the unsubscribe above, so this is
        // a fixed snapshot: wait for any OnFocusChangedAsync calls still in flight before releasing
        // the UIA objects they use.
        Task[] pending;
        lock (_backgroundTasksLock) { pending = _backgroundTasks.ToArray(); }
        if (pending.Length > 0)
        {
            try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Timed out waiting for background UIA work to finish before shutdown"); }
        }

        _mouseTracker.Dispose();
        _mouseCommands.Dispose();

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

    /// <summary>
    /// Applies only the settings that changed since last time. Called on the settings-change
    /// thread (file watcher or wizard), possibly while speech is playing, so the engine is not
    /// touched needlessly.
    /// </summary>
    private void ApplySettings(VoxSettings settings)
    {
        lock (_settingsLock)
        {
            var previous = _appliedSettings;
            _appliedSettings = settings;

            // First, so the settings below go to the engine that will speak (the registry also
            // re-applies them to a new engine)
            if (previous is null || previous.SpeechEngine != settings.SpeechEngine)
                _speechEngines.Select(settings.SpeechEngine);
            if (previous is null || previous.SpeechRateWpm != settings.SpeechRateWpm)
                _speechEngine.SetRate(settings.SpeechRateWpm);
            if (previous is null || previous.SpeechPitch != settings.SpeechPitch)
                _speechEngine.SetPitch(settings.SpeechPitch);
            if (previous is null || previous.SpeechVolume != settings.SpeechVolume)
                _speechEngine.SetVolume(settings.SpeechVolume);
            // A cleared voice goes back to the default one (at startup there is nothing to undo)
            if (previous is null ? !string.IsNullOrEmpty(settings.VoiceName) : previous.VoiceName != settings.VoiceName)
                _speechEngine.SetVoice(settings.VoiceName ?? string.Empty);
            if (previous is null || previous.AudioCuesEnabled != settings.AudioCuesEnabled)
                _audioCuePlayer.IsEnabled = settings.AudioCuesEnabled;
            if (previous is null || previous.AudioOutputDevice != settings.AudioOutputDevice)
                _audioCuePlayer.OutputDevice = settings.AudioOutputDevice;
            if (previous is null || previous.EarconScheme != settings.EarconScheme)
                _audioCuePlayer.Scheme = settings.EarconScheme;
            if (previous is null || previous.KeepAudioDeviceAwake != settings.KeepAudioDeviceAwake)
                _audioCuePlayer.KeepAwake = settings.KeepAudioDeviceAwake;
            if (previous is null || previous.AudioDucking != settings.AudioDucking)
                _duckingController.SetMode(settings.AudioDucking);
            if (previous is null || previous.ModifierKey != settings.ModifierKey)
                _keyboardHook.ScreenReaderModifier = settings.ModifierKey;
            // At startup the keymap was loaded for the configured layout already
            if (previous is not null && previous.KeyboardLayout != settings.KeyboardLayout)
                _keyInputDispatcher.SetKeyMap(ServiceRegistration.LoadKeyMap(settings.KeyboardLayout, _logger));
            if (previous is null || previous.StartAtLogon != settings.StartAtLogon)
            {
                try
                {
                    StartAtLogon.Apply(settings.StartAtLogon, _startupRegistration,
                        StartAtLogon.CommandFor(AppContext.BaseDirectory), _runPolicy);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not change the start-at-logon registration");
                }
            }
        }
    }

    private void OnRawKeyReceived(object? sender, RawKeyEvent e)
    {
        _browseModeController.HandleRawKey(e);
        TrackBackground(_focusedTextMonitor.HandleRawKey(e));
        _terminalMonitor.HandleRawKey(e);
    }

    private void OnNavigationCommandReceived(object? sender, NavigationCommandEvent e)
    {
        if (e.Command == NavigationCommand.ToggleSpeechViewer)
        {
            _speechViewer.Toggle();
            return;
        }
        if (e.Command == NavigationCommand.CopySettingsToSecureScreens)
        {
            var message = SecureScreenSettings.Copy(_settingsManager, _settings.CurrentValue, _runPolicy);
            _speechQueue.Enqueue(new Utterance(message, SpeechPriority.Interrupt));
            return;
        }
        if (!_whereAmI.TryHandle(e.Command) && !_objectNavigation.TryHandle(e.Command) && !_review.TryHandle(e.Command)
            && !_mouseTracker.TryHandle(e.Command) && !_mouseCommands.TryHandle(e.Command)
            && !_settingsRing.TryHandle(e.Command) && !_speechHistoryCommands.TryHandle(e.Command)
            && !_sleepMode.TryHandle(e.Command))
            _browseModeController.HandleCommand(e.Command);
    }

    private void OnFocusChangedProcessed(object? sender, FocusChangedEvent e)
    {
        _browseModeController.HandleFocusChanged(e);
        _whereAmI.HandleFocusChanged(e);
        _review.HandleFocusChanged();
        _objectNavigation.HandleFocusChanged();
        TrackBackground(_focusedTextMonitor.HandleFocusChanged());
        TrackBackground(_documentTracker.OnFocusChangedAsync(_browseModeController.FocusSequence));
        TrackBackground(FollowFocusForTextAsync());
    }

    /// <summary>Finds the newly focused text control, then lets the terminal monitor take its first snapshot.</summary>
    private async Task FollowFocusForTextAsync()
    {
        await IgnoreUiaFailure(_uiaEventSubscriber.FollowFocusForTextAsync(), "following focus for caret events").ConfigureAwait(false);
        await _terminalMonitor.HandleFocusChangedAsync().ConfigureAwait(false);
    }

    /// <summary>Awaits background UIA work whose failure (timeout, element gone) only needs logging.</summary>
    private async Task IgnoreUiaFailure(Task task, string what)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Shutting down
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "UIA error while {What}", what);
        }
    }

    private void OnCaretMovedProcessed(object? sender, CaretMovedEvent e)
    {
        _review.HandleCaretMoved();
        TrackBackground(_focusedTextMonitor.HandleCaretMovedAsync(e));
    }

    private void OnMenuEventProcessed(object? sender, MenuEvent e) => _menuTracker.Handle(e.Kind);

    /// <summary>Said before a focus announcement: the window title when focus moved into another window, then menu context.</summary>
    private string? FocusContext(FocusChangedEvent focus)
    {
        var parts = new[] { _foregroundWindowMonitor.FocusContext(focus), _menuTracker.TakeContext() }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var context = string.Join(". ", parts);
        return context.Length == 0 ? null : context;
    }

    private void OnProgressChangedProcessed(object? sender, ProgressChangedEvent e)
    {
        if (_progressReporter.Evaluate(e) is not { } report)
            return;
        if (report.Speech is { } speech)
            _speechQueue.Enqueue(new Utterance(speech, SpeechPriority.Normal));
        if (report.ToneHz is { } hz)
            _audioCuePlayer.PlayTone(hz, 40);
    }

    private void OnForegroundWindowChangedProcessed(object? sender, ForegroundWindowChangedEvent e)
    {
        // Keys typed into the new window are decided from the cached sleep state
        _sleepMode.IsAsleepNow();
        TrackBackground(_dialogReader.HandleForegroundWindowChangedAsync(e));
    }

    private void OnTextEditedProcessed(object? sender, TextEditedEvent e)
    {
        TrackBackground(_focusedTextMonitor.HandleTextEditedAsync(e));
        TrackBackground(_terminalMonitor.HandleTextEditedAsync(e));
    }

    private void OnStructureChangedProcessed(object? sender, StructureChangedEvent e) =>
        _documentTracker.OnStructureChanged(e.RuntimeId);

    private void OnDocumentChangedProcessed(object? sender, DocumentChangedEvent e) =>
        _browseModeController.HandleDocumentChanged(e);

    private void OnSubtreeChangedProcessed(object? sender, SubtreeChangedEvent e) =>
        _browseModeController.HandleSubtreeChanged(e);

    private void OnFocusInDocumentProcessed(object? sender, FocusInDocumentEvent e) =>
        _browseModeController.HandleFocusInDocument(e);

    private void OnElementsListClosedProcessed(object? sender, ElementsListClosedEvent e) =>
        _browseModeController.HandleElementsListClosed(e);

    private void OnFindPromptClosedProcessed(object? sender, FindPromptClosedEvent e) =>
        _browseModeController.HandleFindPromptClosed(e);

    private void OnPropertyChangedProcessed(object? sender, PropertyChangedEvent e)
    {
        _browseModeController.HandlePropertyChanged(e);

        // State, name and value changes alter what the buffer says about the element (Chromium
        // raises no StructureChanged for them): re-capture it. The tracker debounces bursts.
        if (BrowseModeController.ChangesBufferText(e.PropertyId))
            _documentTracker.OnStructureChanged(e.RuntimeId);
    }

    private void OnElementSelectedProcessed(object? sender, ElementSelectedEvent e) =>
        _browseModeController.HandleElementSelected(e);

    private void OnQuitRequested(object? sender, EventArgs e) => _ = QuitAsync();

    private async Task QuitAsync()
    {
        _logger.LogInformation("Quit requested by the user");
        try
        {
            // Say goodbye (briefly) before the speech engine is stopped
            await _speechQueue.EnqueueAndWaitAsync(new Utterance("Vox exiting", SpeechPriority.Interrupt))
                .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
        }
        _lifetime.StopApplication();
    }

    private void OnSetupRequested(object? sender, EventArgs e)
    {
        if (!_runPolicy.AllowSetupWizard)
        {
            _speechQueue.Enqueue(new Utterance("Not available on this screen", SpeechPriority.Interrupt));
            return;
        }
        _ = RunSetupAgainAsync();
    }

    /// <summary>
    /// Runs the first-run wizard again. Browse-mode key handling is paused meanwhile, so the
    /// wizard's keys (arrows, Enter, digits) aren't also taken as navigation commands.
    /// </summary>
    private async Task RunSetupAgainAsync()
    {
        if (Interlocked.Exchange(ref _setupRunning, 1) == 1)
            return;
        try
        {
            _sayAllController.Cancel();
            _keyInputDispatcher.Stop();
            // Page speech (focus, live regions, notifications) must not talk over the wizard
            _speechQueue.Suspend();
            await _firstRunWizard.RunAsync(_lifetime.ApplicationStopping).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "First-run wizard failed");
        }
        finally
        {
            _speechQueue.Resume();
            if (!_lifetime.ApplicationStopping.IsCancellationRequested)
                _keyInputDispatcher.Start();
            Interlocked.Exchange(ref _setupRunning, 0);
        }
    }

    private void OnModeChanged(object? sender, InteractionMode mode) =>
        _keyInputDispatcher.SetMode(mode);

    private void OnEscapeGoesToPageChanged(object? sender, bool value) =>
        _keyInputDispatcher.SetEscapeGoesToPage(value);

    private void OnDocumentActiveChanged(object? sender, bool active) =>
        _keyInputDispatcher.SetDocumentActive(active);
}
