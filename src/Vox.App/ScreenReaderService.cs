using System.ComponentModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.AddOns;
using Vox.Core.Ai;
using Vox.Core.AppModules;
using Vox.Core.Audio;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Localization;
using Vox.Core.Navigation;
using Vox.Core.Ocr;
using Vox.Core.Pipeline;
using Vox.Core.Remote;
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
    private readonly SettingsCommands _settingsCommands;
    private readonly Vox.Core.Updates.UpdateCommands _updateCommands;
    private readonly AppModuleManager _appModules;
    private readonly ProfileManager _profiles;
    private readonly ProfileCommands _profileCommands;
    private VoxTrayIcon? _trayIcon;
    // Pause speech (Insert+S): nothing is said until it is pressed again
    private volatile bool _speechPaused;
    private readonly SleepMode _sleepMode;
    private readonly MouseCommands _mouseCommands;
    private readonly OcrCommands _ocr;
    private readonly AiCommands _ai;
    private readonly AddOnManager _addOns;
    private readonly AddOnCommands _addOnCommands;
    private readonly RemoteAccessSession _remoteSession;
    private readonly RemoteCommands _remoteCommands;
    private readonly Vox.Core.Switching.SettingsImportCommands _importCommands;
    private readonly Vox.Core.Labels.CustomLabelCommands _labelCommands;
    private readonly SiteSettingsCommands _siteSettingsCommands;
    private readonly Vox.Core.Labels.AutomaticLabeler? _automaticLabels;
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
    private readonly RemoteSessionMonitor? _remoteSessions;
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
        SettingsCommands settingsCommands,
        Vox.Core.Updates.UpdateCommands updateCommands,
        AppModuleManager appModules,
        ProfileManager profiles,
        ProfileCommands profileCommands,
        OcrCommands ocr,
        AiCommands ai,
        AddOnManager addOns,
        AddOnCommands addOnCommands,
        RemoteAccessSession remoteSession,
        RemoteCommands remoteCommands,
        Vox.Core.Switching.SettingsImportCommands importCommands,
        Vox.Core.Labels.CustomLabelCommands labelCommands,
        SiteSettingsCommands siteSettingsCommands,
        RunPolicy? runPolicy = null,
        RemoteSessionMonitor? remoteSessions = null,
        Vox.Core.Labels.AutomaticLabeler? automaticLabels = null)
    {
        _automaticLabels = automaticLabels;
        _addOns = addOns;
        _addOnCommands = addOnCommands;
        _remoteSession = remoteSession;
        _remoteCommands = remoteCommands;
        _importCommands = importCommands;
        _labelCommands = labelCommands;
        _siteSettingsCommands = siteSettingsCommands;
        _remoteSessions = remoteSessions;
        _profiles = profiles;
        _profileCommands = profileCommands;
        _appModules = appModules;
        _updateCommands = updateCommands;
        _settingsCommands = settingsCommands;
        _review = review;
        _mouseTracker = mouseTracker;
        _speechEngines = speechEngines;
        _settingsRing = settingsRing;
        _speechHistoryCommands = speechHistoryCommands;
        _sleepMode = sleepMode;
        _mouseCommands = mouseCommands;
        _ocr = ocr;
        _ai = ai;
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
        _eventPipeline.VoxDialogClosedProcessed += OnVoxDialogClosedProcessed;
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
        _eventPipeline.CustomLabelProvider = _browseModeController.CustomLabelFor;
        // The foreground application's module, then the add-ons' global plug-ins, may drop events,
        // change focus announcements and see every event once handled
        _eventPipeline.EventFilter = e => _appModules.ShouldProcess(e) && _addOns.ShouldProcess(e);
        _eventPipeline.FocusAnnouncementFormatter = FormatFocusAnnouncement;
        _eventPipeline.EventProcessed += OnEventProcessed;
        _eventPipeline.AddOnCommandReceived += OnAddOnCommandReceived;
        // The active add-on app module's commands have keys only while it is active
        _appModules.ActiveModuleChanged += OnActiveModuleChanged;
        // Configuration profiles follow the foreground application and Say All
        _appModules.ForegroundAppChanged += OnForegroundAppChanged;
        _sayAllController.ReadingChanged += OnSayAllReadingChanged;
        _profiles.Switched += OnProfileSwitched;
        // ... and the web site being browsed has its own settings over them
        _browseModeController.SiteChanged += OnSiteChanged;
        // In browse mode the review cursor reviews the virtual buffer
        _review.BrowseTether = _browseModeController.ReviewTether;
        // While browsing, text recognition reads the graphic at the cursor or the page
        _ocr.BrowseTarget = _browseModeController.RecognitionTarget;
        // ... and so does describing; the summary reads the page being browsed
        _ai.BrowseTarget = _browseModeController.RecognitionTarget;
        _ai.BrowseDocument = _browseModeController.BrowsedDocument;
        // Ask the page: any page with focus; moving to the answer happens on the pipeline thread
        _ai.PageDocument = _browseModeController.ActiveDocument;
        _ai.GoToElement = node => _eventPipeline.Post(new PipelineWorkEvent(DateTimeOffset.UtcNow,
            () => _browseModeController.GoToElement(node)));
        _ai.MainContentKey = () => KeyNameFor(NavigationCommand.MoveToMainContent);
        _browseModeController.KeyNameFor = KeyNameFor;

        // Keep key resolution in sync with the browse/focus mode and document focus
        _navigationManager.ModeChanged += OnModeChanged;
        _browseModeController.DocumentActiveChanged += OnDocumentActiveChanged;
        _browseModeController.EscapeGoesToPageChanged += OnEscapeGoesToPageChanged;
        _browseModeController.QuitRequested += OnQuitRequested;
        _browseModeController.SetupRequested += OnSetupRequested;
        _keyInputDispatcher.SetMode(_navigationManager.CurrentMode);
        _keyInputDispatcher.SetDocumentActive(_browseModeController.IsDocumentActive);

        // Sleep mode: nothing is said, and keys go to the application, while it has focus; and
        // nothing at all while speech is paused
        _speechQueue.IsMuted = () => _speechPaused || _sleepMode.IsAsleepNow();
        _keyInputDispatcher.IsAsleep = () => _sleepMode.IsAsleepCached;

        // Start key input dispatcher (subscribes to keyboard hook, installs key suppression)
        _keyInputDispatcher.Start();

        // The notification-area icon and its menu (not on the secure screens' desktop)
        if (!_runPolicy.IsSecure)
        {
            try { _trayIcon = new VoxTrayIcon(command => _eventPipeline.Post(new NavigationCommandEvent(DateTimeOffset.UtcNow, command))); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not show the notification-area icon"); }
        }

        // Pick up a browser that already has focus
        TrackBackground(_documentTracker.OnFocusChangedAsync());

        // Add-ons start on the pipeline thread, where they are called (none in safe mode)
        _eventPipeline.Post(new PipelineWorkEvent(DateTimeOffset.UtcNow, LoadAddOns));

        // The daily update check, a while after startup so it never delays it
        _ = CheckForUpdatesLaterAsync(_lifetime.ApplicationStopping);

        // Announce startup
        _speechQueue.Enqueue(new Utterance(
            hookInstalled
                ? VoxStrings.Get("App.Ready")
                : VoxStrings.Get("App.ReadyWithoutKeyboard"),
            SpeechPriority.Normal));

        VoxMenu.SafeMode = _runPolicy.SafeMode;
        if (_runPolicy.SafeMode && !_runPolicy.IsSecure)
            _speechQueue.Enqueue(new Utterance(VoxStrings.Get("AddOns.SafeModeStartup"), SpeechPriority.Normal));

        // Said once: in a remote session the keys and audio go through the remote-desktop client
        if (_remoteSessions is not null)
        {
            if (_remoteSessions.Current.Announcement is { } remote && !_runPolicy.IsSecure)
                _speechQueue.Enqueue(new Utterance(remote, SpeechPriority.Normal));
            _remoteSessions.Changed += OnRemoteSessionChanged;
            try { Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch; }
            catch (Exception ex) { _logger.LogDebug(ex, "Session changes won't be noticed"); }
        }
    }

    // Reconnecting to the session from Remote Desktop, or at the computer again
    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is Microsoft.Win32.SessionSwitchReason.RemoteConnect or Microsoft.Win32.SessionSwitchReason.ConsoleConnect)
            _remoteSessions?.Refresh();
    }

    private void OnRemoteSessionChanged(object? sender, RemoteSession session)
    {
        ApplySettings(_settings.CurrentValue);
        if (!_runPolicy.IsSecure)
            _speechQueue.Enqueue(new Utterance(session.Announcement ?? VoxStrings.Get("App.LocalSession"), SpeechPriority.Normal));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Vox Screen Reader stopping");

        if (_remoteSessions is not null)
        {
            _remoteSessions.Changed -= OnRemoteSessionChanged;
            try { Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch; }
            catch (Exception ex) { _logger.LogDebug(ex, "Could not unsubscribe from session changes"); }
        }

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
        _eventPipeline.VoxDialogClosedProcessed -= OnVoxDialogClosedProcessed;
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
        _eventPipeline.EventFilter = null;
        _eventPipeline.FocusAnnouncementFormatter = null;
        _eventPipeline.EventProcessed -= OnEventProcessed;
        _eventPipeline.AddOnCommandReceived -= OnAddOnCommandReceived;
        _appModules.ActiveModuleChanged -= OnActiveModuleChanged;
        _appModules.ForegroundAppChanged -= OnForegroundAppChanged;
        _sayAllController.ReadingChanged -= OnSayAllReadingChanged;
        _profiles.Switched -= OnProfileSwitched;
        _browseModeController.SiteChanged -= OnSiteChanged;
        _automaticLabels?.Cancel();
        _appModules.Stop();
        // A remote session ends (keys held on this computer are released), then add-ons close
        _remoteSession.Dispose();
        try { _addOns.UnloadAll(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Add-ons failed to close"); }
        _review.BrowseTether = null;
        _ocr.BrowseTarget = null;
        _ai.BrowseTarget = null;
        _ai.BrowseDocument = null;
        _ai.PageDocument = null;
        _ai.GoToElement = null;
        _ai.MainContentKey = null;
        _ai.Cancel();
        _browseModeController.KeyNameFor = null;
        _speechQueue.IsMuted = null;
        _keyInputDispatcher.IsAsleep = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
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
        // As the settings work in this session (a remote one turns some audio settings off)
        if (_remoteSessions is not null)
            settings = _remoteSessions.Current.Adjust(settings);
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
            if (previous is null || previous.Language != settings.Language)
                ApplyLanguage(previous, settings);
            if (previous is null || previous.ModifierKey != settings.ModifierKey)
                _keyboardHook.ScreenReaderModifier = settings.ModifierKey;
            // At startup the keymap was loaded for the configured layout already
            if (previous is not null && previous.KeyboardLayout != settings.KeyboardLayout)
                _keyInputDispatcher.SetKeyMap(ServiceRegistration.LoadKeyMap(settings.KeyboardLayout, _logger, _runPolicy));
            // The page is laid out again in the new way
            if (previous is not null && previous.ScreenLayout != settings.ScreenLayout)
                _documentTracker.RequestRecapture(null);
            // An app module turned on or off: picked again on the pipeline thread
            if (previous is not null && !previous.DisabledAppModules.SequenceEqual(settings.DisabledAppModules))
                _eventPipeline.Post(new PipelineWorkEvent(DateTimeOffset.UtcNow, _appModules.Reselect));
            if (previous is null || previous.StartAtLogon != settings.StartAtLogon)
            {
                try
                {
                    StartAtLogon.Apply(settings.StartAtLogon, _startupRegistration,
                        StartAtLogon.CommandFor(AppContext.BaseDirectory), _runPolicy, VoxPaths.IsPortable);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not change the start-at-logon registration");
                }
            }
        }
    }

    /// <summary>
    /// Vox's strings follow the Language setting at once. When it changes and the braille table is
    /// still the old language's usual one, braille follows too.
    /// </summary>
    private void ApplyLanguage(VoxSettings? previous, VoxSettings settings)
    {
        var before = VoxStrings.EffectiveCulture;
        VoxStrings.Culture = VoxLanguages.Culture(settings.Language);
        if (settings.Language is not null && VoxStrings.Culture is null)
            _logger.LogWarning("Unknown language {Language} in the settings; following Windows", settings.Language);
        if (previous is null)
            return;
        var table = VoxLanguages.BrailleTableFor(VoxStrings.EffectiveCulture);
        if (string.Equals(settings.BrailleTable, VoxLanguages.BrailleTableFor(before), StringComparison.OrdinalIgnoreCase)
            && !string.Equals(settings.BrailleTable, table, StringComparison.OrdinalIgnoreCase)
            && _settings is SettingsMonitor monitor)
        {
            // Not from inside this settings change
            _ = Task.Run(() => monitor.UpdateSettings(monitor.CurrentValue with { BrailleTable = table }));
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
        // Braille commands are BrailleService's
        if (e.Command is NavigationCommand.BraillePanLeft or NavigationCommand.BraillePanRight or NavigationCommand.CycleBrailleTether)
            return;
        // The foreground application's own commands
        if (_appModules.TryHandle(e.Command))
            return;
        if (e.Command == NavigationCommand.ToggleSpeechViewer)
        {
            _speechViewer.Toggle();
            return;
        }
        switch (e.Command)
        {
            case NavigationCommand.TogglePauseSpeech:
                if (_speechPaused)
                {
                    _speechPaused = false;
                    _speechQueue.Enqueue(new Utterance(VoxStrings.Get("App.SpeechOn"), SpeechPriority.Interrupt));
                }
                else
                {
                    // Said before speech stops
                    _speechQueue.Enqueue(new Utterance(VoxStrings.Get("App.SpeechPaused"), SpeechPriority.Interrupt));
                    _speechPaused = true;
                }
                return;
            case NavigationCommand.ExitVox:
                _logger.LogInformation("Exit chosen from the Vox menu");
                _lifetime.StopApplication();
                return;
            case NavigationCommand.OpenUserGuide:
                OpenUserGuide();
                return;
            case NavigationCommand.RunTutorial:
                if (_runPolicy.AllowSetupWizard)
                    _ = RunWizardAsync(lessonsOnly: true);
                else
                    _speechQueue.Enqueue(new Utterance(VoxStrings.Get("Ocr.NotOnThisScreen"), SpeechPriority.Interrupt));
                return;
        }
        if (e.Command == NavigationCommand.ToggleInputHelp)
        {
            // The dispatcher has switched already, as the key was handled
            var state = _keyInputDispatcher.IsInputHelpOn ? VoxStrings.Get("App.InputHelpOn") : VoxStrings.Get("App.InputHelpOff");
            _speechQueue.Enqueue(new Utterance(state, SpeechPriority.Interrupt));
            return;
        }
        if (e.Command == NavigationCommand.CopySettingsToSecureScreens)
        {
            var message = SecureScreenSettings.Copy(_settingsManager, _settings.CurrentValue, _runPolicy);
            _speechQueue.Enqueue(new Utterance(message, SpeechPriority.Interrupt));
            return;
        }
        // Stopping speech also stops waiting for an AI answer
        if (e.Command == NavigationCommand.StopSpeech)
            _ai.Cancel();
        if (!_whereAmI.TryHandle(e.Command) && !_objectNavigation.TryHandle(e.Command) && !_review.TryHandle(e.Command)
            && !_mouseTracker.TryHandle(e.Command) && !_mouseCommands.TryHandle(e.Command) && !_ocr.TryHandle(e.Command)
            && !_ai.TryHandle(e.Command)
            && !_settingsRing.TryHandle(e.Command) && !_speechHistoryCommands.TryHandle(e.Command)
            && !_sleepMode.TryHandle(e.Command) && !_settingsCommands.TryHandle(e.Command)
            && !_updateCommands.TryHandle(e.Command) && !_profileCommands.TryHandle(e.Command)
            && !_addOnCommands.TryHandle(e.Command) && !_remoteCommands.TryHandle(e.Command)
            && !_importCommands.TryHandle(e.Command) && !_labelCommands.TryHandle(e.Command)
            && !_siteSettingsCommands.TryHandle(e.Command))
            _browseModeController.HandleCommand(e.Command);
    }

    /// <summary>The key <paramref name="command"/> is on in the keymap in use ("Insert+R"), or null.</summary>
    private string? KeyNameFor(NavigationCommand command)
    {
        // The browse-mode key: what is offered is offered while browsing
        var key = _keyInputDispatcher.KeyMap.Bindings
            .Where(b => b.Command == command)
            .Select(b => b.Key)
            .OrderBy(k => k.Mode == InteractionMode.Browse ? 0 : 1)
            .Cast<KeyMapKey?>()
            .FirstOrDefault();
        return key is { } found
            ? InputHelp.KeyName(found.Modifiers, found.VkCode, screenReaderKey: InputHelp.ScreenReaderKeyName(_settings.CurrentValue.ModifierKey))
            : null;
    }

    private void OnFocusChangedProcessed(object? sender, FocusChangedEvent e)
    {
        // First, so the focused application's module formats this very announcement
        _appModules.Refresh();
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

    private void OnEventProcessed(object? sender, ScreenReaderEvent e)
    {
        _appModules.OnEvent(e);
        _addOns.OnEvent(e);
    }

    /// <summary>The focus announcement as the active app module, then the add-ons' plug-ins, have it.</summary>
    private string? FormatFocusAnnouncement(FocusChangedEvent focus, string announcement)
    {
        var text = _appModules.FormatFocusAnnouncement(focus, announcement);
        return string.IsNullOrEmpty(text) ? text : _addOns.FormatFocusAnnouncement(focus, text);
    }

    private void OnAddOnCommandReceived(object? sender, AddOnCommandEvent e) => _addOnCommands.Run(e);

    private void OnActiveModuleChanged(object? sender, Vox.Core.AppModules.IAppModule? module) => _addOns.RefreshKeys();

    private void LoadAddOns()
    {
        try { _addOns.LoadAll(); }
        catch (Exception ex) { _logger.LogError(ex, "Add-ons could not be loaded"); }
    }

    private void OnForegroundAppChanged(object? sender, string? processName) => _profiles.SetForegroundApp(processName);

    private void OnSayAllReadingChanged(object? sender, bool reading) => _profiles.SetSayAll(reading);

    private void OnSiteChanged(object? sender, string? site) => _profiles.SetSite(site);

    private void OnProfileSwitched(object? sender, string? profile)
    {
        if (_settings.CurrentValue.AnnounceProfileSwitches)
            _speechQueue.Enqueue(new Utterance(ProfileCommands.SwitchAnnouncement(profile), SpeechPriority.Normal));
    }

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
        _appModules.Update(e.ProcessId, e.WindowHandle);
        TrackBackground(_dialogReader.HandleForegroundWindowChangedAsync(e));
    }

    private void OnTextEditedProcessed(object? sender, TextEditedEvent e)
    {
        TrackBackground(_focusedTextMonitor.HandleTextEditedAsync(e));
        TrackBackground(_terminalMonitor.HandleTextEditedAsync(e));
    }

    private void OnStructureChangedProcessed(object? sender, StructureChangedEvent e) =>
        _documentTracker.OnStructureChanged(e.RuntimeId);

    private void OnDocumentChangedProcessed(object? sender, DocumentChangedEvent e)
    {
        _browseModeController.HandleDocumentChanged(e);
        // Unlabelled graphics and icon buttons are described in the background once the whole page is there
        if (!e.IsPartial && e.Document is { } document && _automaticLabels is { } labels)
            TrackBackground(labels.OnDocumentLoaded(document));
    }

    private void OnSubtreeChangedProcessed(object? sender, SubtreeChangedEvent e) =>
        _browseModeController.HandleSubtreeChanged(e);

    private void OnFocusInDocumentProcessed(object? sender, FocusInDocumentEvent e) =>
        _browseModeController.HandleFocusInDocument(e);

    private void OnElementsListClosedProcessed(object? sender, ElementsListClosedEvent e) =>
        _browseModeController.HandleElementsListClosed(e);

    private async Task CheckForUpdatesLaterAsync(CancellationToken stopping)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stopping).ConfigureAwait(false);
            await _updateCommands.CheckQuietlyAsync(stopping).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Update check failed");
        }
    }

    /// <summary>Opens the user guide installed beside Vox in the default browser (not on secure screens).</summary>
    private void OpenUserGuide()
    {
        var guide = Path.Combine(AppContext.BaseDirectory, "docs", "guide", "index.html");
        if (_runPolicy.IsSecure || !File.Exists(guide))
        {
            _speechQueue.Enqueue(new Utterance(_runPolicy.IsSecure ? VoxStrings.Get("App.GuideNotAvailableHere") : VoxStrings.Get("App.GuideNotInstalled"), SpeechPriority.Interrupt));
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(guide) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open the user guide");
            _speechQueue.Enqueue(new Utterance(VoxStrings.Get("App.GuideOpenFailed"), SpeechPriority.Interrupt));
        }
    }

    private void OnVoxDialogClosedProcessed(object? sender, VoxDialogClosedEvent e) =>
        _browseModeController.EndOwnDialog(e.QuietFocusReturn);

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
            await _speechQueue.EnqueueAndWaitAsync(new Utterance(VoxStrings.Get("App.Exiting"), SpeechPriority.Interrupt))
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
            _speechQueue.Enqueue(new Utterance(VoxStrings.Get("Ocr.NotOnThisScreen"), SpeechPriority.Interrupt));
            return;
        }
        _ = RunWizardAsync(lessonsOnly: false);
    }

    /// <summary>
    /// Runs the first-run wizard again, or just its practice lessons. Vox's key handling is
    /// paused meanwhile, so the wizard's keys aren't also taken as commands.
    /// </summary>
    private async Task RunWizardAsync(bool lessonsOnly)
    {
        if (Interlocked.Exchange(ref _setupRunning, 1) == 1)
            return;
        try
        {
            _sayAllController.Cancel();
            _keyInputDispatcher.Stop();
            // Page speech (focus, live regions, notifications) must not talk over the wizard
            _speechQueue.Suspend();
            if (lessonsOnly)
                await _firstRunWizard.RunLessonsAsync(_lifetime.ApplicationStopping).ConfigureAwait(false);
            else
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
