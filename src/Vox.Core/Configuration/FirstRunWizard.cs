using Microsoft.Extensions.Logging;
using Vox.Core.Input;
using Vox.Core.Localization;
using Vox.Core.Speech;
using Vox.Core.Switching;

namespace Vox.Core.Configuration;

/// <summary>
/// Speech-only first-run wizard. Guides a new user through 8 steps to configure Vox.
/// Triggered when VoxSettings.FirstRunCompleted == false.
/// Re-runnable from settings.
///
/// Steps:
///   1. Welcome — Enter to continue, Escape to skip
///      (then, when NVDA or JAWS settings are found: 1 or 2 brings them over, Enter goes on)
///   2. Speech rate — Up/Down to adjust live, speaks test sentence
///   3. Voice selection — Up/Down to cycle voices
///   4. Verbosity — 1=Beginner, 2=Intermediate, 3=Advanced
///   5. Keyboard layout — 1=Desktop, 2=Laptop (which also makes CapsLock the modifier)
///   6. Modifier key — 1=Insert, 2=CapsLock
///   7. Tutorial — practice H, K, Enter, Insert+Space
///   8. Completion — "Press Insert+F1 for help anytime"
/// </summary>
public sealed class FirstRunWizard
{
    // Finer steps at everyday rates, coarser ones up to the fastest
    private const int RateStep = 10;
    private const int FastRateStep = 25;
    private const int FastRateFromWpm = 400;
    private const int MinRateWpm = ISpeechEngine.MinSupportedWpm;
    private const int DefaultMaxRateWpm = 450;

    private readonly ISpeechEngine _speechEngine;
    private readonly SettingsManager _settingsManager;
    private readonly SettingsMonitor _settingsMonitor;
    private readonly IKeyboardHook _keyboardHook;
    private readonly ILogger<FirstRunWizard> _logger;

    private TaskCompletionSource<KeyEvent>? _keyWaiter;
    // The keys in use, for the practice lessons (none: no lessons offered)
    private readonly Func<GestureEditor>? _lessonKeys;
    // While a practice lesson waits for a key, every key is the lesson's
    private volatile bool _swallowAllKeys;
    // Offers to bring over NVDA's or JAWS's settings (none: not offered, e.g. on secure screens)
    private readonly ISettingsImportOffer? _importOffer;
    private ImportReport? _importReport;

    /// <summary>
    /// Whether the wizard is waiting for a key now. Keys pressed while it isn't are not its own
    /// (they pass through to the system); tests use this to press keys only once a step listens.
    /// </summary>
    public bool IsWaitingForKey => Volatile.Read(ref _keyWaiter) is not null;

    public FirstRunWizard(
        ISpeechEngine speechEngine,
        SettingsManager settingsManager,
        SettingsMonitor settingsMonitor,
        IKeyboardHook keyboardHook,
        ILogger<FirstRunWizard> logger,
        Func<GestureEditor>? lessonKeys = null,
        ISettingsImportOffer? importOffer = null)
    {
        _lessonKeys = lessonKeys;
        _importOffer = importOffer;
        _speechEngine = speechEngine;
        _settingsManager = settingsManager;
        _settingsMonitor = settingsMonitor;
        _keyboardHook = keyboardHook;
        _logger = logger;
    }

    /// <summary>
    /// Runs the wizard. Returns when the wizard completes or is skipped.
    /// The caller should have the keyboard hook installed before calling this.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting first-run wizard");

        _keyboardHook.KeyPressed += OnKeyPressed;

        // Keep the wizard's keys away from whatever application has focus — but only while the
        // wizard is actually waiting for one, so keys pressed between prompts reach their app
        var previousFilter = _keyboardHook.SuppressionFilter;
        _keyboardHook.SuppressionFilter = e =>
            (_swallowAllKeys || IsWizardKey(e)) && Volatile.Read(ref _keyWaiter) is not null ? KeyDecision.Swallow : KeyDecision.Pass;

        try
        {
            var settings = _settingsMonitor.CurrentValue;

            // Step 1: Welcome
            bool proceed = await RunWelcomeStepAsync(cancellationToken);
            if (!proceed)
            {
                await SpeakAsync(VoxStrings.Format("Wizard.Skipped", RerunKeyDescription), cancellationToken);
                settings = settings with { FirstRunCompleted = true };
                _settingsMonitor.UpdateSettings(settings);
                return;
            }

            // From here on Escape leaves the wizard, keeping what was chosen so far
            _exitOnEscape = true;

            // NVDA's or JAWS's settings, when found: the steps after it start from them
            settings = await RunImportStepAsync(settings, cancellationToken);

            // Step 2: Speech rate
            settings = await RunSpeechRateStepAsync(settings, cancellationToken);

            // Step 3: Voice selection
            settings = await RunVoiceSelectionStepAsync(settings, cancellationToken);

            // Step 4: Verbosity
            settings = await RunVerbosityStepAsync(settings, cancellationToken);

            // Step 5: Keyboard layout
            settings = await RunKeyboardLayoutStepAsync(settings, cancellationToken);

            // Step 6: Modifier key
            settings = await RunModifierKeyStepAsync(settings, cancellationToken);

            // Step 7: Tutorial
            await RunTutorialStepAsync(settings, cancellationToken);

            // Step 8: Completion
            settings = settings with { FirstRunCompleted = true };
            _settingsMonitor.UpdateSettings(settings);
            await SpeakAsync(VoxStrings.Get("Wizard.Complete"), cancellationToken);

            _logger.LogInformation("First-run wizard completed");
        }
        catch (WizardExitException ex)
        {
            // Escape, or nobody answered for a while: keep the choices made so far and stop
            _logger.LogInformation("First-run wizard ended early ({Reason})", ex.Message);
            var saved = _settingsMonitor.CurrentValue with { FirstRunCompleted = true };
            _settingsMonitor.UpdateSettings(saved);

            // A rate or voice that was only being previewed must not stay in use
            _speechEngine.SetRate(saved.SpeechRateWpm);
            _speechEngine.SetVoice(saved.VoiceName ?? string.Empty);
            try
            {
                await SpeakAsync(VoxStrings.Get("Wizard.Ended"), cancellationToken);
            }
            catch (OperationCanceledException) { }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("First-run wizard cancelled");
        }
        finally
        {
            _exitOnEscape = false;
            Volatile.Write(ref _keyWaiter, null);
            _keyboardHook.SuppressionFilter = previousFilter;
            _keyboardHook.KeyPressed -= OnKeyPressed;
            // The import's full report, to read once the wizard has let go of the keyboard
            if (Interlocked.Exchange(ref _importReport, null) is { } report)
                _importOffer?.ShowReport(report);
        }
    }

    /// <summary>
    /// How long a step waits for a key before the wizard gives up (saving the choices so far),
    /// so it can never hold the keyboard indefinitely.
    /// </summary>
    public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How the key that runs setup again is spoken (RunSetup in the keymap: modifier+Ctrl+S).</summary>
    private string RerunKeyDescription =>
        VoxStrings.Format("Wizard.RerunKey", InputHelp.ScreenReaderKeyName(_settingsMonitor.CurrentValue.ModifierKey));

    private bool _exitOnEscape;

    /// <summary>A setting's value as the wizard says it ("Beginner", "Laptop"): the Enum.&lt;type&gt;.&lt;value&gt; string.</summary>
    private static string EnumName<T>(T value) where T : struct, Enum =>
        VoxStrings.GetOrDefault($"Enum.{typeof(T).Name}.{value}", value.ToString());

    private sealed class WizardExitException(string reason) : Exception(reason);

    // -------------------------------------------------------------------------
    // Step implementations
    // -------------------------------------------------------------------------

    private async Task<bool> RunWelcomeStepAsync(CancellationToken cancellationToken)
    {
        // Timeout after 30 seconds — auto-skip if no user interaction
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            var key = await PromptAsync(VoxStrings.Get("Wizard.Welcome"), timeoutCts.Token);

            while (true)
            {
                if (key.VkCode == VirtualKeys.Return)
                    return true;
                if (key.VkCode == VirtualKeys.Escape)
                    return false;
                key = await WaitForKeyDownAsync(timeoutCts.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("First-run wizard timed out — skipping setup");
            return false;
        }
    }

    /// <summary>The fastest rate the speech engine speaks (up to <see cref="ISpeechEngine.MaxSupportedWpm"/>).</summary>
    private int MaxRateWpm
    {
        get
        {
            int max = _speechEngine.MaxRateWpm;
            // 0: the engine doesn't say
            return max <= 0 ? DefaultMaxRateWpm : Math.Clamp(max, MinRateWpm, ISpeechEngine.MaxSupportedWpm);
        }
    }

    private async Task<VoxSettings> RunImportStepAsync(VoxSettings settings, CancellationToken cancellationToken)
    {
        if (_importOffer is null)
            return settings;
        IReadOnlyList<ImportSource> sources;
        try
        {
            sources = _importOffer.FindSources();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not look for other screen readers' settings");
            return settings;
        }
        if (sources.Count == 0)
            return settings;

        var prompt = sources.Count == 1
            ? VoxStrings.Format("Wizard.ImportOffer", ImportReportText.SourceName(sources[0]))
            : VoxStrings.Get("Wizard.ImportOfferBoth");
        var key = await PromptAsync(prompt, cancellationToken);
        ImportSource source;
        while (true)
        {
            if (key.VkCode == VirtualKeys.Return)
                return settings;
            if (key.VkCode is VirtualKeys.D1 or VirtualKeys.NumPad1)
            {
                source = sources[0];
                break;
            }
            if (key.VkCode is VirtualKeys.D2 or VirtualKeys.NumPad2 && sources.Count > 1)
            {
                source = sources[1];
                break;
            }
            key = await WaitForKeyDownAsync(cancellationToken);
        }

        ImportReport? report;
        try
        {
            report = await Task.Run(() => _importOffer.Import(source), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Import of {Source} settings failed", source);
            report = null;
        }
        if (report is null)
        {
            await SpeakAsync(VoxStrings.Get("Wizard.ImportFailed"), cancellationToken);
            return settings;
        }
        _importReport = report;
        await SpeakAsync(VoxStrings.Format("Wizard.ImportDone", report.Imported.Count, report.Skipped.Count), cancellationToken);
        return _settingsMonitor.CurrentValue;
    }

    private async Task<VoxSettings> RunSpeechRateStepAsync(VoxSettings settings, CancellationToken cancellationToken)
    {
        int rate = settings.SpeechRateWpm;
        _speechEngine.SetRate(rate);

        var key = await PromptAsync(VoxStrings.Format("Wizard.RatePrompt", rate), cancellationToken);

        while (key.VkCode != VirtualKeys.Return)
        {
            if (key.VkCode == VirtualKeys.Up || key.VkCode == VirtualKeys.Down)
            {
                rate = key.VkCode == VirtualKeys.Up
                    ? Math.Min(rate + (rate >= FastRateFromWpm ? FastRateStep : RateStep), MaxRateWpm)
                    : Math.Max(rate - (rate > FastRateFromWpm ? FastRateStep : RateStep), MinRateWpm);
                _speechEngine.SetRate(rate);
                key = await PromptAsync(VoxStrings.Format("Wizard.RateSample", rate, VoxStrings.Get("Wizard.SampleSentence")), cancellationToken);
                continue;
            }
            key = await WaitForKeyDownAsync(cancellationToken);
        }

        settings = settings with { SpeechRateWpm = rate };
        _settingsMonitor.UpdateSettings(settings);
        await SpeakAsync(VoxStrings.Format("Wizard.RateSet", rate), cancellationToken);
        return settings;
    }

    private async Task<VoxSettings> RunVoiceSelectionStepAsync(VoxSettings settings, CancellationToken cancellationToken)
    {
        var voices = _speechEngine.GetAvailableVoices();
        if (voices.Count == 0)
        {
            await SpeakAsync(VoxStrings.Get("Wizard.NoVoices"), cancellationToken);
            return settings;
        }

        // Start at the voice actually speaking: the configured one, else the engine's default
        var current = !string.IsNullOrEmpty(settings.VoiceName) ? settings.VoiceName : _speechEngine.CurrentVoice;
        int currentIndex = 0;
        for (int i = 0; i < voices.Count; i++)
        {
            if (voices[i] == current) { currentIndex = i; break; }
        }
        int initialIndex = currentIndex;

        var key = await PromptAsync(VoxStrings.Format("Wizard.VoicePrompt", voices.Count, voices[currentIndex]), cancellationToken);

        while (key.VkCode != VirtualKeys.Return)
        {
            if (key.VkCode == VirtualKeys.Up || key.VkCode == VirtualKeys.Down)
            {
                currentIndex = key.VkCode == VirtualKeys.Up
                    ? (currentIndex + 1) % voices.Count
                    : (currentIndex - 1 + voices.Count) % voices.Count;
                _speechEngine.SetVoice(voices[currentIndex]);
                key = await PromptAsync(VoxStrings.Format("Wizard.VoiceSample", voices[currentIndex], VoxStrings.Get("Wizard.SampleSentence")), cancellationToken);
                continue;
            }
            key = await WaitForKeyDownAsync(cancellationToken);
        }

        // Keeping the voice that was already speaking changes nothing (and saves nothing, so the
        // default voice keeps following the engine's choice)
        if (currentIndex != initialIndex)
        {
            settings = settings with { VoiceName = voices[currentIndex] };
            _settingsMonitor.UpdateSettings(settings);
        }
        await SpeakAsync(VoxStrings.Format("Wizard.VoiceSet", voices[currentIndex]), cancellationToken);
        return settings;
    }

    private async Task<VoxSettings> RunVerbosityStepAsync(VoxSettings settings, CancellationToken cancellationToken)
    {
        var key = await PromptAsync(VoxStrings.Get("Wizard.VerbosityPrompt"), cancellationToken);

        for (; ; key = await WaitForKeyDownAsync(cancellationToken))
        {
            if (key.VkCode == VirtualKeys.Return)
                break; // Keep current verbosity

            if (key.VkCode == VirtualKeys.D1 || key.VkCode == VirtualKeys.NumPad1)
            {
                settings = settings with { VerbosityLevel = VerbosityLevel.Beginner };
                break;
            }
            if (key.VkCode == VirtualKeys.D2 || key.VkCode == VirtualKeys.NumPad2)
            {
                settings = settings with { VerbosityLevel = VerbosityLevel.Intermediate };
                break;
            }
            if (key.VkCode == VirtualKeys.D3 || key.VkCode == VirtualKeys.NumPad3)
            {
                settings = settings with { VerbosityLevel = VerbosityLevel.Advanced };
                break;
            }
        }

        _settingsMonitor.UpdateSettings(settings);
        await SpeakAsync(VoxStrings.Format("Wizard.VerbositySet", EnumName(settings.VerbosityLevel)), cancellationToken);
        return settings;
    }

    private async Task<VoxSettings> RunKeyboardLayoutStepAsync(VoxSettings settings, CancellationToken cancellationToken)
    {
        var key = await PromptAsync(VoxStrings.Format("Wizard.LayoutPrompt", EnumName(settings.KeyboardLayout)), cancellationToken);

        for (; ; key = await WaitForKeyDownAsync(cancellationToken))
        {
            if (key.VkCode == VirtualKeys.Return)
                break; // Keep current layout

            if (key.VkCode == VirtualKeys.D1 || key.VkCode == VirtualKeys.NumPad1)
            {
                settings = settings with { KeyboardLayout = KeyboardLayout.Desktop };
                break;
            }
            if (key.VkCode == VirtualKeys.D2 || key.VkCode == VirtualKeys.NumPad2)
            {
                // The laptop layout is meant for Caps Lock; the next step can still change it
                settings = settings with { KeyboardLayout = KeyboardLayout.Laptop, ModifierKey = ModifierKey.CapsLock };
                break;
            }
        }

        _settingsMonitor.UpdateSettings(settings);
        await SpeakAsync(VoxStrings.Format("Wizard.LayoutSet", EnumName(settings.KeyboardLayout)), cancellationToken);
        return settings;
    }

    private async Task<VoxSettings> RunModifierKeyStepAsync(VoxSettings settings, CancellationToken cancellationToken)
    {
        var key = await PromptAsync(
            VoxStrings.Format("Wizard.ModifierPrompt", InputHelp.ScreenReaderKeyName(settings.ModifierKey)), cancellationToken);

        for (; ; key = await WaitForKeyDownAsync(cancellationToken))
        {
            if (key.VkCode == VirtualKeys.Return)
                break; // Keep current modifier key

            if (key.VkCode == VirtualKeys.D1 || key.VkCode == VirtualKeys.NumPad1)
            {
                settings = settings with { ModifierKey = ModifierKey.Insert };
                break;
            }
            if (key.VkCode == VirtualKeys.D2 || key.VkCode == VirtualKeys.NumPad2)
            {
                settings = settings with { ModifierKey = ModifierKey.CapsLock };
                break;
            }
        }

        _settingsMonitor.UpdateSettings(settings);
        await SpeakAsync(VoxStrings.Format("Wizard.ModifierSet", EnumName(settings.ModifierKey)), cancellationToken);
        return settings;
    }

    private async Task RunTutorialStepAsync(VoxSettings settings, CancellationToken cancellationToken)
    {
        var modifier = InputHelp.ScreenReaderKeyName(settings.ModifierKey);
        var sayAllKey = settings.KeyboardLayout == KeyboardLayout.Laptop ? "A" : VoxStrings.Get("Key.DownArrow");
        var key = await PromptAsync(VoxStrings.Format("Wizard.TutorialPrompt", modifier, sayAllKey), cancellationToken);

        while (key.VkCode != VirtualKeys.Return)
            key = await WaitForKeyDownAsync(cancellationToken);

        if (_lessonKeys is null)
            return;
        key = await PromptAsync(VoxStrings.Get("Wizard.PractiseOffer"), cancellationToken);
        while (key.VkCode is not (VirtualKeys.Return or VirtualKeys.D1 or VirtualKeys.NumPad1))
            key = await WaitForKeyDownAsync(cancellationToken);
        if (key.VkCode != VirtualKeys.Return)
            await RunLessonMenuAsync(settings, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Practice lessons
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs the practice lessons on their own (the Vox menu's "Practice lessons"): the user picks
    /// a topic and presses each command's key in turn. Escape leaves a lesson, and the menu.
    /// The caller should have the keyboard hook installed and Vox's own key handling paused.
    /// </summary>
    public async Task RunLessonsAsync(CancellationToken cancellationToken = default)
    {
        _keyboardHook.KeyPressed += OnKeyPressed;
        var previousFilter = _keyboardHook.SuppressionFilter;
        _keyboardHook.SuppressionFilter = e =>
            (_swallowAllKeys || IsWizardKey(e)) && Volatile.Read(ref _keyWaiter) is not null ? KeyDecision.Swallow : KeyDecision.Pass;
        try
        {
            await RunLessonMenuAsync(_settingsMonitor.CurrentValue, cancellationToken);
        }
        catch (WizardExitException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Volatile.Write(ref _keyWaiter, null);
            _keyboardHook.SuppressionFilter = previousFilter;
            _keyboardHook.KeyPressed -= OnKeyPressed;
        }
    }

    private async Task RunLessonMenuAsync(VoxSettings settings, CancellationToken cancellationToken)
    {
        if (_lessonKeys is null)
            return;
        var keys = _lessonKeys();
        var modifierName = InputHelp.ScreenReaderKeyName(settings.ModifierKey);
        var lessons = TutorialLessons.All;
        var choices = string.Join(VoxStrings.Get("Lesson.ChoiceSeparator"),
            lessons.Select((l, i) => VoxStrings.Format("Lesson.Choice", i + 1, l.Topic)));
        // Escape leaves the lessons, not the whole setup
        bool exitOnEscape = _exitOnEscape;
        _exitOnEscape = false;
        try
        {
            while (true)
            {
                var key = await PromptAsync(VoxStrings.Format("Lesson.Menu", choices), cancellationToken);
                int lesson = key.VkCode switch
                {
                    >= VirtualKeys.D1 and <= VirtualKeys.D9 => key.VkCode - VirtualKeys.D1,
                    >= VirtualKeys.NumPad1 and <= VirtualKeys.NumPad9 => key.VkCode - VirtualKeys.NumPad1,
                    _ => -1,
                };
                if (key.VkCode == VirtualKeys.Escape)
                    break;
                if (lesson >= 0 && lesson < lessons.Count)
                    await RunLessonAsync(lessons[lesson], keys, modifierName, cancellationToken);
            }
        }
        finally
        {
            _exitOnEscape = exitOnEscape;
        }
        await SpeakAsync(VoxStrings.Get("Lesson.Finished"), cancellationToken);
    }

    /// <summary>The attempts at a step before the lesson says the key and moves on.</summary>
    private const int Attempts = 3;

    private async Task RunLessonAsync(Lesson lesson, GestureEditor keys, string modifierName, CancellationToken cancellationToken)
    {
        _swallowAllKeys = true;
        try
        {
            await SpeakAsync(VoxStrings.Format("Lesson.Start", lesson.Topic, lesson.Introduction), cancellationToken);
            foreach (var step in lesson.Steps)
            {
                if (TutorialLessons.KeyFor(step.Command, keys) is not { } gesture)
                    continue;
                var keyName = gesture.Describe(modifierName);
                var key = await PromptAsync(VoxStrings.Format("Lesson.Ask", step.Instruction, keyName), cancellationToken);
                for (int attempt = 1; ; attempt++)
                {
                    if (key.VkCode == VirtualKeys.Escape && key.Modifiers == KeyModifiers.None)
                        return;
                    if (TutorialLessons.Matches(key, gesture))
                    {
                        await SpeakAsync(VoxStrings.Format("Lesson.Right", step.Done), cancellationToken);
                        break;
                    }
                    if (InputHelp.IsModifierKey(key.VkCode))
                    {
                        key = await WaitForKeyDownAsync(cancellationToken);
                        continue;
                    }
                    if (attempt >= Attempts)
                    {
                        await SpeakAsync(VoxStrings.Format("Lesson.KeyIs", keyName), cancellationToken);
                        break;
                    }
                    var pressed = InputHelp.KeyName(key.Modifiers, key.VkCode, key.IsKeypad, modifierName);
                    key = await PromptAsync(VoxStrings.Format("Lesson.ThatWas", pressed, step.Instruction, keyName), cancellationToken);
                }
            }
            await SpeakAsync(VoxStrings.Format("Lesson.Done", lesson.Topic), cancellationToken);
        }
        finally
        {
            _swallowAllKeys = false;
        }
    }

    // -------------------------------------------------------------------------
    // Key input helpers
    // -------------------------------------------------------------------------

    private void OnKeyPressed(object? sender, KeyEvent e)
    {
        if (!e.IsKeyDown) return;
        // Take the waiter so the suppression filter stops swallowing until the next wait
        Interlocked.Exchange(ref _keyWaiter, null)?.TrySetResult(e);
    }

    private static bool IsWizardKey(KeyEvent e) => e.VkCode is
        VirtualKeys.Return or VirtualKeys.Escape or VirtualKeys.Up or VirtualKeys.Down or
        VirtualKeys.D1 or VirtualKeys.D2 or VirtualKeys.D3 or
        VirtualKeys.NumPad1 or VirtualKeys.NumPad2 or VirtualKeys.NumPad3;

    private async Task<KeyEvent> WaitForKeyDownAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<KeyEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _keyWaiter, tcs);

        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        waitCts.CancelAfter(InactivityTimeout);
        using var registration = waitCts.Token.Register(() =>
        {
            Interlocked.CompareExchange(ref _keyWaiter, null, tcs);
            tcs.TrySetCanceled(waitCts.Token);
        });

        KeyEvent key;
        try
        {
            key = await tcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WizardExitException("no key pressed");
        }

        if (_exitOnEscape && key.VkCode == VirtualKeys.Escape)
            throw new WizardExitException("escape pressed");
        return key;
    }

    /// <summary>
    /// Speaks <paramref name="text"/> while already listening for a key; a key press cuts the
    /// prompt short. Returns the first key pressed (during or after the prompt).
    /// </summary>
    private async Task<KeyEvent> PromptAsync(string text, CancellationToken cancellationToken)
    {
        var keyTask = WaitForKeyDownAsync(cancellationToken);
        var speakTask = SpeakAsync(text, cancellationToken);

        var first = await Task.WhenAny(keyTask, speakTask).ConfigureAwait(false);
        if (first == keyTask && !speakTask.IsCompleted)
            _speechEngine.Cancel();

        try
        {
            await speakTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Prompt interrupted by the key press
        }

        return await keyTask.ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Speech helpers
    // -------------------------------------------------------------------------

    private async Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Wizard: {Text}", text);
        var utterance = new Utterance(text, SpeechPriority.Interrupt);
        await _speechEngine.SpeakAsync(utterance, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Virtual key code constants used by the wizard.
/// </summary>
internal static class VirtualKeys
{
    public const int Return = 0x0D;
    public const int Escape = 0x1B;
    public const int Up = 0x26;
    public const int Down = 0x28;
    public const int D1 = 0x31;
    public const int D2 = 0x32;
    public const int D3 = 0x33;
    public const int D9 = 0x39;
    public const int NumPad1 = 0x61;
    public const int NumPad2 = 0x62;
    public const int NumPad3 = 0x63;
    public const int NumPad9 = 0x69;
}
