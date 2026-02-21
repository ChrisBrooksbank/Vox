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

        try
        {
            var settings = _settingsMonitor.CurrentValue;

            // Step 1: Welcome
            bool proceed = await RunWelcomeStepAsync(cancellationToken);
            if (!proceed)
            {
                await SpeakAsync("Setup skipped. You can re-run it from settings.", cancellationToken);
                settings = settings with { FirstRunCompleted = true };
                _settingsMonitor.UpdateSettings(settings);
                return;
            }

            // Step 2: Speech rate
            settings = await RunSpeechRateStepAsync(settings, cancellationToken);

            // Step 3: Voice selection
            settings = await RunVoiceSelectionStepAsync(settings, cancellationToken);

            // Step 4: Verbosity
            settings = await RunVerbosityStepAsync(settings, cancellationToken);

            // Step 5: Modifier key
            settings = await RunModifierKeyStepAsync(settings, cancellationToken);

            // Step 6: Tutorial
            await RunTutorialStepAsync(cancellationToken);

            // Step 7: Completion
            settings = settings with { FirstRunCompleted = true };
            _settingsMonitor.UpdateSettings(settings);
            await SpeakAsync(
                "Setup complete. Press Insert F1 for help anytime. Welcome to Vox.",
                cancellationToken);

            _logger.LogInformation("First-run wizard completed");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("First-run wizard cancelled");
        }
        finally
        {
            _keyboardHook.KeyPressed -= OnKeyPressed;
        }
    }

    // -------------------------------------------------------------------------
    // Step implementations
    // -------------------------------------------------------------------------

    private async Task<bool> RunWelcomeStepAsync(CancellationToken cancellationToken)
    {
        await SpeakAsync(
            "Welcome to Vox screen reader. " +
            "This guided setup will help you configure speech rate, voice, verbosity, and modifier key. " +
            "Press Enter to begin, or Escape to skip setup.",
            cancellationToken);

        // Timeout after 30 seconds — auto-skip if no user interaction
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            while (true)
            {
                var key = await WaitForKeyDownAsync(timeoutCts.Token);
                if (key.VkCode == VirtualKeys.Return)
                    return true;
                if (key.VkCode == VirtualKeys.Escape)
                    return false;
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

        await SpeakAsync(
            $"Step 1 of 5: Speech rate. Current rate is {rate} words per minute. " +
            "Press Up to increase, Down to decrease, or Enter to accept.",
            cancellationToken);

        while (true)
        {
            var key = await WaitForKeyDownAsync(cancellationToken);

            if (key.VkCode == VirtualKeys.Return)
                break;

            if (key.VkCode == VirtualKeys.Up)
            {
                rate = Math.Min(rate + RateStep, MaxRateWpm);
                _speechEngine.SetRate(rate);
                _speechEngine.Cancel();
                await SpeakAsync($"{rate} words per minute. The quick brown fox jumps over the lazy dog.", cancellationToken);
            }
            else if (key.VkCode == VirtualKeys.Down)
            {
                rate = Math.Max(rate - RateStep, MinRateWpm);
                _speechEngine.SetRate(rate);
                _speechEngine.Cancel();
                await SpeakAsync($"{rate} words per minute. The quick brown fox jumps over the lazy dog.", cancellationToken);
            }
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

        int currentIndex = 0;
        if (!string.IsNullOrEmpty(settings.VoiceName))
        {
            var idx = -1;
            for (int i = 0; i < voices.Count; i++)
            {
                if (voices[i] == settings.VoiceName) { idx = i; break; }
            }
            if (idx >= 0) currentIndex = idx;
        }

        await SpeakAsync(
            $"Step 2 of 5: Voice selection. {voices.Count} voices available. " +
            $"Current voice: {voices[currentIndex]}. " +
            "Press Up or Down to cycle voices, Enter to accept.",
            cancellationToken);

        while (true)
        {
            var key = await WaitForKeyDownAsync(cancellationToken);

            if (key.VkCode == VirtualKeys.Return)
                break;

            if (key.VkCode == VirtualKeys.Up)
            {
                currentIndex = (currentIndex + 1) % voices.Count;
                _speechEngine.SetVoice(voices[currentIndex]);
                _speechEngine.Cancel();
                await SpeakAsync($"{voices[currentIndex]}. The quick brown fox jumps over the lazy dog.", cancellationToken);
            }
            else if (key.VkCode == VirtualKeys.Down)
            {
                currentIndex = (currentIndex - 1 + voices.Count) % voices.Count;
                _speechEngine.SetVoice(voices[currentIndex]);
                _speechEngine.Cancel();
                await SpeakAsync($"{voices[currentIndex]}. The quick brown fox jumps over the lazy dog.", cancellationToken);
            }
        }

        settings = settings with { VoiceName = voices[currentIndex] };
        _settingsMonitor.UpdateSettings(settings);
        await SpeakAsync($"Voice set to {voices[currentIndex]}.", cancellationToken);
        return settings;
    }

    private async Task<VoxSettings> RunVerbosityStepAsync(VoxSettings settings, CancellationToken cancellationToken)
    {
        await SpeakAsync(
            "Step 3 of 5: Verbosity level. " +
            "Press 1 for Beginner — all element details announced, recommended for new users. " +
            "Press 2 for Intermediate — control type and essential state. " +
            "Press 3 for Advanced — minimal announcements. " +
            "Press Enter to keep the current setting.",
            cancellationToken);

        while (true)
        {
            var key = await WaitForKeyDownAsync(cancellationToken);

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
        await SpeakAsync(
            "Step 4 of 5: Modifier key. " +
            "Press 1 for Insert key, recommended. " +
            "Press 2 for Caps Lock. " +
            "Press Enter to keep the current setting.",
            cancellationToken);

        while (true)
        {
            var key = await WaitForKeyDownAsync(cancellationToken);

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

    private async Task RunTutorialStepAsync(CancellationToken cancellationToken)
    {
        await SpeakAsync(
            "Step 5 of 5: Quick tutorial. " +
            "In browse mode, press H to jump to the next heading. " +
            "Press K to jump to the next link. " +
            "Press Enter to activate the focused element. " +
            "Press Insert Space to toggle between browse and focus modes. " +
            "Press Insert Escape to stop speech at any time. " +
            "Press Enter to continue.",
            cancellationToken);

        while (true)
        {
            var key = await WaitForKeyDownAsync(cancellationToken);
            if (key.VkCode == VirtualKeys.Return)
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Key input helpers
    // -------------------------------------------------------------------------

    private void OnKeyPressed(object? sender, KeyEvent e)
    {
        if (!e.IsKeyDown) return;
        Volatile.Read(ref _keyWaiter)?.TrySetResult(e);
    }

    private Task<KeyEvent> WaitForKeyDownAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<KeyEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _keyWaiter, tcs);

        cancellationToken.Register(() =>
        {
            Volatile.Write(ref _keyWaiter, null);
            tcs.TrySetCanceled(cancellationToken);
        });

        return tcs.Task;
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
