using Microsoft.Extensions.Logging;
using Vox.Core.Input;
using Vox.Core.Speech;

namespace Vox.Core.Configuration;

/// <summary>
/// Speech-only first-run wizard. Guides a new user through 7 steps to configure Vox.
/// Triggered when VoxSettings.FirstRunCompleted == false.
/// Re-runnable from settings.
///
/// Steps:
///   1. Welcome — Enter to continue, Escape to skip
///   2. Speech rate — Up/Down to adjust live, speaks test sentence
///   3. Voice selection — Up/Down to cycle voices
///   4. Verbosity — 1=Beginner, 2=Intermediate, 3=Advanced
///   5. Modifier key — 1=Insert, 2=CapsLock
///   6. Tutorial — practice H, K, Enter, Insert+Space
///   7. Completion — "Press Insert+F1 for help anytime"
/// </summary>
public sealed class FirstRunWizard
{
    private const int RateStep = 10;
    private const int MinRateWpm = 150;
    private const int MaxRateWpm = 450;

    private readonly ISpeechEngine _speechEngine;
    private readonly SettingsManager _settingsManager;
    private readonly SettingsMonitor _settingsMonitor;
    private readonly IKeyboardHook _keyboardHook;
    private readonly ILogger<FirstRunWizard> _logger;

    private TaskCompletionSource<KeyEvent>? _keyWaiter;

    public FirstRunWizard(
        ISpeechEngine speechEngine,
        SettingsManager settingsManager,
        SettingsMonitor settingsMonitor,
        IKeyboardHook keyboardHook,
        ILogger<FirstRunWizard> logger)
    {
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
            IsWizardKey(e) && Volatile.Read(ref _keyWaiter) is not null ? KeyDecision.Swallow : KeyDecision.Pass;

        try
        {
            var settings = _settingsMonitor.CurrentValue;

            // Step 1: Welcome
            bool proceed = await RunWelcomeStepAsync(cancellationToken);
            if (!proceed)
            {
                await SpeakAsync($"Setup skipped. You can run it again at any time with {RerunKeyDescription}.", cancellationToken);
                settings = settings with { FirstRunCompleted = true };
                _settingsMonitor.UpdateSettings(settings);
                return;
            }

            // From here on Escape leaves the wizard, keeping what was chosen so far
            _exitOnEscape = true;

            // Step 2: Speech rate
            settings = await RunSpeechRateStepAsync(settings, cancellationToken);

            // Step 3: Voice selection
            settings = await RunVoiceSelectionStepAsync(settings, cancellationToken);

            // Step 4: Verbosity
            settings = await RunVerbosityStepAsync(settings, cancellationToken);

            // Step 5: Modifier key
            settings = await RunModifierKeyStepAsync(settings, cancellationToken);

            // Step 6: Tutorial
            await RunTutorialStepAsync(settings, cancellationToken);

            // Step 7: Completion
            settings = settings with { FirstRunCompleted = true };
            _settingsMonitor.UpdateSettings(settings);
            await SpeakAsync(
                "Setup complete. Welcome to Vox.",
                cancellationToken);

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
                await SpeakAsync("Setup ended. Your choices so far are saved.", cancellationToken);
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
        }
    }

    /// <summary>
    /// How long a step waits for a key before the wizard gives up (saving the choices so far),
    /// so it can never hold the keyboard indefinitely.
    /// </summary>
    public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How the key that runs setup again is spoken (RunSetup in the keymap: modifier+Ctrl+S).</summary>
    private string RerunKeyDescription =>
        (_settingsMonitor.CurrentValue.ModifierKey == ModifierKey.CapsLock ? "Caps Lock" : "Insert") + " Control S";

    private bool _exitOnEscape;

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
            var key = await PromptAsync(
                "Welcome to Vox screen reader. " +
                "This guided setup will help you configure speech rate, voice, verbosity, and modifier key. " +
                "Press Enter to begin, or Escape to skip setup.",
                timeoutCts.Token);

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

    private async Task<VoxSettings> RunSpeechRateStepAsync(VoxSettings settings, CancellationToken cancellationToken)
    {
        int rate = settings.SpeechRateWpm;
        _speechEngine.SetRate(rate);

        var key = await PromptAsync(
            $"Step 1 of 5: Speech rate. Current rate is {rate} words per minute. " +
            "Press Up to increase, Down to decrease, or Enter to accept.",
            cancellationToken);

        while (key.VkCode != VirtualKeys.Return)
        {
            if (key.VkCode == VirtualKeys.Up || key.VkCode == VirtualKeys.Down)
            {
                rate = key.VkCode == VirtualKeys.Up
                    ? Math.Min(rate + RateStep, MaxRateWpm)
                    : Math.Max(rate - RateStep, MinRateWpm);
                _speechEngine.SetRate(rate);
                key = await PromptAsync($"{rate} words per minute. The quick brown fox jumps over the lazy dog.", cancellationToken);
                continue;
            }
            key = await WaitForKeyDownAsync(cancellationToken);
        }

        settings = settings with { SpeechRateWpm = rate };
        _settingsMonitor.UpdateSettings(settings);
        await SpeakAsync($"Speech rate set to {rate} words per minute.", cancellationToken);
        return settings;
    }

    private async Task<VoxSettings> RunVoiceSelectionStepAsync(VoxSettings settings, CancellationToken cancellationToken)
    {
        var voices = _speechEngine.GetAvailableVoices();
        if (voices.Count == 0)
        {
            await SpeakAsync("Step 2 of 5: No additional voices found. Continuing with default voice.", cancellationToken);
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

        var key = await PromptAsync(
            $"Step 2 of 5: Voice selection. {voices.Count} voices available. " +
            $"Current voice: {voices[currentIndex]}. " +
            "Press Up or Down to cycle voices, Enter to accept.",
            cancellationToken);

        while (key.VkCode != VirtualKeys.Return)
        {
            if (key.VkCode == VirtualKeys.Up || key.VkCode == VirtualKeys.Down)
            {
                currentIndex = key.VkCode == VirtualKeys.Up
                    ? (currentIndex + 1) % voices.Count
                    : (currentIndex - 1 + voices.Count) % voices.Count;
                _speechEngine.SetVoice(voices[currentIndex]);
                key = await PromptAsync($"{voices[currentIndex]}. The quick brown fox jumps over the lazy dog.", cancellationToken);
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
        await SpeakAsync($"Voice set to {voices[currentIndex]}.", cancellationToken);
        return settings;
    }

    private async Task<VoxSettings> RunVerbosityStepAsync(VoxSettings settings, CancellationToken cancellationToken)
    {
        var key = await PromptAsync(
            "Step 3 of 5: Verbosity level. " +
            "Press 1 for Beginner — all element details announced, recommended for new users. " +
            "Press 2 for Intermediate — control type and essential state. " +
            "Press 3 for Advanced — minimal announcements. " +
            "Press Enter to keep the current setting.",
            cancellationToken);

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
        await SpeakAsync($"Verbosity set to {settings.VerbosityLevel}.", cancellationToken);
        return settings;
    }

    private async Task<VoxSettings> RunModifierKeyStepAsync(VoxSettings settings, CancellationToken cancellationToken)
    {
        var key = await PromptAsync(
            "Step 4 of 5: Modifier key. " +
            "Press 1 for Insert key, recommended. " +
            "Press 2 for Caps Lock. " +
            "Press Enter to keep the current setting.",
            cancellationToken);

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
        await SpeakAsync($"Modifier key set to {settings.ModifierKey}.", cancellationToken);
        return settings;
    }

    private async Task RunTutorialStepAsync(VoxSettings settings, CancellationToken cancellationToken)
    {
        var modifier = settings.ModifierKey == ModifierKey.CapsLock ? "Caps Lock" : "Insert";
        var key = await PromptAsync(
            "Step 5 of 5: Quick tutorial. " +
            "In browse mode, press H to jump to the next heading. " +
            "Press K to jump to the next link. " +
            "Press Enter to activate the current element. " +
            $"Press {modifier} Space to toggle between browse and focus modes. " +
            $"Press {modifier} Down Arrow to read from the current position. " +
            "Press Control to stop speech at any time. " +
            $"Press {modifier} Q twice to exit Vox, and {modifier} Control S to run this setup again. " +
            "Press Enter to continue.",
            cancellationToken);

        while (key.VkCode != VirtualKeys.Return)
            key = await WaitForKeyDownAsync(cancellationToken);
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
    public const int NumPad1 = 0x61;
    public const int NumPad2 = 0x62;
    public const int NumPad3 = 0x63;
}
