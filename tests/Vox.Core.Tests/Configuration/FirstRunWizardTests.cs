using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Speech;
using Vox.Core.Switching;
using Xunit;

namespace Vox.Core.Tests.Configuration;

/// <summary>
/// Tests for FirstRunWizard. Uses a mock keyboard hook to simulate key presses,
/// and a mock speech engine to capture speech output without hardware dependency.
/// </summary>
[Collection("SettingsTests")]
public class FirstRunWizardTests : IDisposable
{
    // Back up and restore the user settings file so our tests don't pollute it
    private readonly string? _userSettingsBackup;

    public FirstRunWizardTests()
    {
        var userPath = SettingsManager.DefaultUserSettingsPath;
        if (File.Exists(userPath))
        {
            _userSettingsBackup = userPath + ".bak_" + Guid.NewGuid().ToString("N");
            File.Copy(userPath, _userSettingsBackup, overwrite: true);
        }
    }

    public void Dispose()
    {
        var userPath = SettingsManager.DefaultUserSettingsPath;
        try
        {
            if (_userSettingsBackup != null && File.Exists(_userSettingsBackup))
            {
                File.Copy(_userSettingsBackup, userPath, overwrite: true);
                File.Delete(_userSettingsBackup);
            }
            else
            {
                // No original file — remove whatever we created
                if (File.Exists(userPath)) File.Delete(userPath);
            }
        }
        catch { }
    }
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private sealed class FakeKeyboardHook : IKeyboardHook
    {
        public event EventHandler<KeyEvent>? KeyPressed;

        public Func<KeyEvent, KeyDecision>? SuppressionFilter { get; set; }
        public ModifierKey ScreenReaderModifier { get; set; }

        public void Install() { }
        public void Uninstall() { }

        public void SimulateKeyDown(int vkCode)
        {
            KeyPressed?.Invoke(this, new KeyEvent
            {
                VkCode = vkCode,
                Modifiers = KeyModifiers.None,
                IsKeyDown = true,
                Timestamp = (uint)Environment.TickCount
            });
        }
    }

    private static (FirstRunWizard wizard, FakeKeyboardHook hook, Mock<ISpeechEngine> engine, SettingsMonitor monitor, SettingsManager manager)
        CreateWizard(VoxSettings? initialSettings = null, ISettingsImportOffer? importOffer = null)
    {
        var engineMock = new Mock<ISpeechEngine>();
        var spokenTexts = new List<string>();

        engineMock
            .Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        engineMock
            .Setup(e => e.GetAvailableVoices())
            .Returns(new List<string> { "Voice One", "Voice Two", "Voice Three" });

        var hook = new FakeKeyboardHook();

        // Write initial settings to temp file used as default-settings fallback.
        // We write to the temp path (used as defaultSettingsPath), NOT to UserSettingsPath.
        // This avoids contaminating the global %APPDATA%/Vox/settings.json.
        var tempPath = Path.GetTempFileName();
        var settingsToWrite = initialSettings ?? new VoxSettings();
        var json = System.Text.Json.JsonSerializer.Serialize(settingsToWrite, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });
        File.WriteAllText(tempPath, json);

        var logger = NullLogger<SettingsManager>.Instance;

        // Use an isolated, never-existing user settings path instead of the real
        // %APPDATA%/Vox/settings.json. SettingsMonitor's FileSystemWatcher watches
        // UserSettingsPath for the life of the test: pointing it at the real path (even
        // briefly, while moving it aside and back) races the watcher against whatever is
        // on disk there and can reload the wizard's in-memory settings mid-test from an
        // unrelated file.
        var isolatedUserPath = Path.Combine(Path.GetTempPath(), "VoxWizardTests_" + Guid.NewGuid().ToString("N") + ".json");
        var manager = new SettingsManager(logger, tempPath, isolatedUserPath);

        var monitorLogger = NullLogger<SettingsMonitor>.Instance;
        var monitor = new SettingsMonitor(manager, monitorLogger);

        var wizard = new FirstRunWizard(
            engineMock.Object,
            manager,
            monitor,
            hook,
            NullLogger<FirstRunWizard>.Instance,
            importOffer: importOffer);

        return (wizard, hook, engineMock, monitor, manager);
    }

    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Waits until the wizard listens for a key (or has finished).</summary>
    private static async Task WaitUntilWaitingAsync(FirstRunWizard wizard, Task wizardTask)
    {
        var deadline = DateTime.UtcNow + ReadyTimeout;
        while (!wizard.IsWaitingForKey && !wizardTask.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The wizard never started waiting for a key");
            await Task.Delay(5);
        }
    }

    /// <summary>
    /// Presses a key once the wizard is listening for one: the wizard only takes keys while a step
    /// waits, so a key pressed in between (while it speaks) would be lost. Does nothing if the
    /// wizard has finished.
    /// </summary>
    private static async Task PressAsync(FirstRunWizard wizard, Task wizardTask, FakeKeyboardHook hook, int vkCode)
    {
        await WaitUntilWaitingAsync(wizard, wizardTask);
        if (!wizardTask.IsCompleted)
            hook.SimulateKeyDown(vkCode);
    }

    // -------------------------------------------------------------------------
    // Skip wizard with Escape
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_EscapeOnWelcome_SetsFirstRunCompletedAndSkips()
    {
        var (wizard, hook, engine, monitor, _) = CreateWizard(
            new VoxSettings { FirstRunCompleted = false });

        // Schedule Escape after a brief delay to let the wizard start speaking
        var wizardTask = wizard.RunAsync();
        await PressAsync(wizard, wizardTask, hook, 0x1B); // Escape

        await wizardTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(monitor.CurrentValue.FirstRunCompleted);
    }

    [Fact]
    public async Task RunAsync_EscapeOnWelcome_SpeaksSkipMessage()
    {
        var (wizard, hook, engine, monitor, _) = CreateWizard(
            new VoxSettings { FirstRunCompleted = false });

        var spokenTexts = new List<string>();
        engine
            .Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Returns((Utterance u, CancellationToken _) =>
            {
                lock (spokenTexts) spokenTexts.Add(u.Text);
                return Task.CompletedTask;
            });

        var wizardTask = wizard.RunAsync();
        await PressAsync(wizard, wizardTask, hook, 0x1B); // Escape

        await wizardTask.WaitAsync(TimeSpan.FromSeconds(5));

        lock (spokenTexts)
        {
            Assert.Contains(spokenTexts, t => t.Contains("skip", StringComparison.OrdinalIgnoreCase));
        }
    }

    // -------------------------------------------------------------------------
    // Complete wizard with Enter through all steps
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_EnterThroughAllSteps_SetsFirstRunCompleted()
    {
        var (wizard, hook, engine, monitor, _) = CreateWizard(
            new VoxSettings { FirstRunCompleted = false });

        // We'll simulate Enter for every step that waits for input
        var wizardTask = wizard.RunAsync();

        // Drive through all steps by pressing Enter repeatedly
        for (int i = 0; i < 10; i++)
        {
            await PressAsync(wizard, wizardTask, hook, 0x0D); // Enter
            if (wizardTask.IsCompleted) break;
        }

        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(monitor.CurrentValue.FirstRunCompleted);
    }

    [Fact]
    public async Task RunAsync_EnterThroughAllSteps_SpeaksCompletionMessage()
    {
        var (wizard, hook, engine, monitor, _) = CreateWizard(
            new VoxSettings { FirstRunCompleted = false });

        var spokenTexts = new List<string>();
        engine
            .Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Returns((Utterance u, CancellationToken _) =>
            {
                lock (spokenTexts) spokenTexts.Add(u.Text);
                return Task.CompletedTask;
            });

        var wizardTask = wizard.RunAsync();

        for (int i = 0; i < 10; i++)
        {
            await PressAsync(wizard, wizardTask, hook, 0x0D); // Enter
            if (wizardTask.IsCompleted) break;
        }

        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));

        lock (spokenTexts)
        {
            Assert.Contains(spokenTexts, t =>
                t.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Welcome", StringComparison.OrdinalIgnoreCase));
        }
    }

    // -------------------------------------------------------------------------
    // Speech rate adjustment
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_UpArrowInRateStep_IncreasesRate()
    {
        var (wizard, hook, engine, monitor, _) = CreateWizard(
            new VoxSettings { FirstRunCompleted = false, SpeechRateWpm = 200 });

        int capturedRate = 0;
        engine
            .Setup(e => e.SetRate(It.IsAny<int>()))
            .Callback((int r) => capturedRate = r);

        var wizardTask = wizard.RunAsync();

        // Welcome step → Enter
        await PressAsync(wizard, wizardTask, hook, 0x0D);

        // Rate step: press Up to increase, then Enter to confirm
        await PressAsync(wizard, wizardTask, hook, 0x26); // Up arrow
        await PressAsync(wizard, wizardTask, hook, 0x0D); // Enter

        // Drive remaining steps with Enter
        for (int i = 0; i < 10; i++)
        {
            await PressAsync(wizard, wizardTask, hook, 0x0D);
            if (wizardTask.IsCompleted) break;
        }

        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(capturedRate > 200 || monitor.CurrentValue.SpeechRateWpm > 200,
            $"Rate should have increased above 200, was: capturedRate={capturedRate}, saved={monitor.CurrentValue.SpeechRateWpm}");
    }

    [Theory]
    [InlineData(900, 400, 3, 475)]  // above 400 the rate moves in steps of 25
    [InlineData(900, 880, 3, 900)]  // up to the engine's fastest rate
    [InlineData(540, 530, 3, 540)]  // SAPI stops at its own maximum
    [InlineData(0, 440, 3, 450)]    // an engine that doesn't say: 450
    public async Task RunAsync_RateStep_GoesUpToTheEnginesFastestRate(int engineMax, int start, int presses, int expected)
    {
        var (wizard, hook, engine, monitor, _) = CreateWizard(
            new VoxSettings { FirstRunCompleted = false, SpeechRateWpm = start });
        engine.Setup(e => e.MaxRateWpm).Returns(engineMax);

        var wizardTask = wizard.RunAsync();
        await PressAsync(wizard, wizardTask, hook, 0x0D); // Welcome
        for (int i = 0; i < presses; i++)
            await PressAsync(wizard, wizardTask, hook, 0x26); // Up
        await PressAsync(wizard, wizardTask, hook, 0x0D); // accept the rate
        await PressAsync(wizard, wizardTask, hook, 0x1B); // and stop there

        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(expected, monitor.CurrentValue.SpeechRateWpm);
    }

    // -------------------------------------------------------------------------
    // Verbosity selection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_SelectVerbosityAdvanced_SavesAdvanced()
    {
        var (wizard, hook, engine, monitor, _) = CreateWizard(
            new VoxSettings { FirstRunCompleted = false });

        var wizardTask = wizard.RunAsync();

        // Welcome → Enter
        await PressAsync(wizard, wizardTask, hook, 0x0D);

        // Rate step → Enter
        await PressAsync(wizard, wizardTask, hook, 0x0D);

        // Voice step → Enter
        await PressAsync(wizard, wizardTask, hook, 0x0D);

        // Verbosity step → press 3 for Advanced
        await PressAsync(wizard, wizardTask, hook, 0x33); // '3'

        // Layout step → 1 for Desktop
        await PressAsync(wizard, wizardTask, hook, 0x31);

        // Modifier step → Enter (1 for Insert)
        await PressAsync(wizard, wizardTask, hook, 0x31); // '1'

        // Tutorial → Enter
        await PressAsync(wizard, wizardTask, hook, 0x0D);

        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(VerbosityLevel.Advanced, monitor.CurrentValue.VerbosityLevel);
    }

    // -------------------------------------------------------------------------
    // Modifier key selection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_SelectModifierCapsLock_SavesCapsLock()
    {
        var (wizard, hook, engine, monitor, _) = CreateWizard(
            new VoxSettings { FirstRunCompleted = false });

        var wizardTask = wizard.RunAsync();

        // Welcome → Enter
        await PressAsync(wizard, wizardTask, hook, 0x0D);
        // Rate → Enter
        await PressAsync(wizard, wizardTask, hook, 0x0D);
        // Voice → Enter
        await PressAsync(wizard, wizardTask, hook, 0x0D);
        // Verbosity → 1
        await PressAsync(wizard, wizardTask, hook, 0x31);
        // Layout → 1 for Desktop
        await PressAsync(wizard, wizardTask, hook, 0x31);
        // Modifier → 2 for CapsLock
        await PressAsync(wizard, wizardTask, hook, 0x32);
        // Tutorial → Enter
        await PressAsync(wizard, wizardTask, hook, 0x0D);

        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ModifierKey.CapsLock, monitor.CurrentValue.ModifierKey);
    }

    // -------------------------------------------------------------------------
    // Keyboard layout selection
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0x0D, ModifierKey.CapsLock)] // Enter keeps the Caps Lock the laptop layout chose
    [InlineData(0x31, ModifierKey.Insert)]   // 1 picks Insert after all
    public async Task RunAsync_SelectLaptopLayout_SavesLaptopWithCapsLockByDefault(int modifierStepKey, ModifierKey expected)
    {
        var (wizard, hook, _, monitor, _) = CreateWizard(
            new VoxSettings { FirstRunCompleted = false });

        var wizardTask = wizard.RunAsync();

        await PressAsync(wizard, wizardTask, hook, 0x0D); // Welcome
        await PressAsync(wizard, wizardTask, hook, 0x0D); // Rate
        await PressAsync(wizard, wizardTask, hook, 0x0D); // Voice
        await PressAsync(wizard, wizardTask, hook, 0x0D); // Verbosity
        await PressAsync(wizard, wizardTask, hook, 0x32); // Layout → 2 for Laptop
        await PressAsync(wizard, wizardTask, hook, modifierStepKey);
        await PressAsync(wizard, wizardTask, hook, 0x0D); // Tutorial

        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(KeyboardLayout.Laptop, monitor.CurrentValue.KeyboardLayout);
        Assert.Equal(expected, monitor.CurrentValue.ModifierKey);
    }

    [Theory]
    [InlineData(0x33, KeyboardLayout.Jaws, ModifierKey.Insert)]          // 3: JAWS desktop
    [InlineData(0x34, KeyboardLayout.JawsLaptop, ModifierKey.CapsLock)]  // 4: JAWS laptop, Caps Lock like JAWS's
    public async Task RunAsync_SelectJawsLayout_SavesIt(int layoutKey, KeyboardLayout expectedLayout, ModifierKey expectedModifier)
    {
        var (wizard, hook, _, monitor, _) = CreateWizard(
            new VoxSettings { FirstRunCompleted = false });

        var wizardTask = wizard.RunAsync();

        await PressAsync(wizard, wizardTask, hook, 0x0D); // Welcome
        await PressAsync(wizard, wizardTask, hook, 0x0D); // Rate
        await PressAsync(wizard, wizardTask, hook, 0x0D); // Voice
        await PressAsync(wizard, wizardTask, hook, 0x0D); // Verbosity
        await PressAsync(wizard, wizardTask, hook, layoutKey);
        await PressAsync(wizard, wizardTask, hook, 0x0D); // Modifier: keep
        await PressAsync(wizard, wizardTask, hook, 0x0D); // Tutorial

        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(expectedLayout, monitor.CurrentValue.KeyboardLayout);
        Assert.Equal(expectedModifier, monitor.CurrentValue.ModifierKey);
    }

    // -------------------------------------------------------------------------
    // Cancellation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_Cancelled_CompletesWithoutThrowingForCaller()
    {
        var (wizard, hook, engine, monitor, _) = CreateWizard(
            new VoxSettings { FirstRunCompleted = false });

        using var cts = new CancellationTokenSource();
        var wizardTask = wizard.RunAsync(cts.Token);

        await WaitUntilWaitingAsync(wizard, wizardTask);
        cts.Cancel();

        // Should complete (possibly with cancellation) within a reasonable time
        var completed = await Task.WhenAny(wizardTask, Task.Delay(3000)) == wizardTask;
        Assert.True(completed, "Wizard should complete when cancellation token is cancelled");
    }

    // -------------------------------------------------------------------------
    // Settings persistence
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_CompletedWizard_PersistsSettingsToDisk()
    {
        // Use CreateWizard to get proper test isolation (no UserSettingsPath contamination)
        var (wizard, hook, engine, monitor, _) = CreateWizard(
            new VoxSettings { FirstRunCompleted = false });

        var wizardTask = wizard.RunAsync();

        // Drive through all steps
        for (int i = 0; i < 10; i++)
        {
            await PressAsync(wizard, wizardTask, hook, 0x0D); // Enter works for all steps now
            if (wizardTask.IsCompleted) break;
        }

        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));

        // Settings should be updated in-memory
        Assert.True(monitor.CurrentValue.FirstRunCompleted);
    }

    // -------------------------------------------------------------------------
    // Keys during prompts and key suppression
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_KeyPressedWhilePromptIsSpeaking_IsHandled()
    {
        var (wizard, hook, engine, monitor, _) = CreateWizard(
            new VoxSettings { FirstRunCompleted = false });

        // Speech that only ends when cancelled, like a long prompt
        var speaking = new TaskCompletionSource();
        engine
            .Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Returns((Utterance u, CancellationToken _) =>
                u.Text.StartsWith("Welcome") ? speaking.Task : Task.CompletedTask);
        engine.Setup(e => e.Cancel()).Callback(() => speaking.TrySetCanceled());

        var wizardTask = wizard.RunAsync();
        await PressAsync(wizard, wizardTask, hook, 0x1B); // Escape while the welcome prompt is still speaking

        await wizardTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(monitor.CurrentValue.FirstRunCompleted);
        engine.Verify(e => e.Cancel(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task RunAsync_SuppressesOnlyWizardKeys_AndRestoresFilter()
    {
        var (wizard, hook, _, _, _) = CreateWizard(
            new VoxSettings { FirstRunCompleted = false });
        Func<KeyEvent, KeyDecision> original = _ => KeyDecision.Pass;
        hook.SuppressionFilter = original;

        var wizardTask = wizard.RunAsync();
        await WaitUntilWaitingAsync(wizard, wizardTask);

        var filter = hook.SuppressionFilter!;
        Assert.True(filter(new KeyEvent { VkCode = 0x0D, IsKeyDown = true }).Suppress);  // Enter
        Assert.True(filter(new KeyEvent { VkCode = 0x26, IsKeyDown = true }).Suppress);  // Up
        Assert.False(filter(new KeyEvent { VkCode = 0x09, IsKeyDown = true }).Suppress); // Tab

        await PressAsync(wizard, wizardTask, hook, 0x1B);
        await wizardTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(original, hook.SuppressionFilter);
    }

    [Fact]
    public async Task RunAsync_EscapeAtALaterStep_EndsWizardAndKeepsChoices()
    {
        var (wizard, hook, _, monitor, _) = CreateWizard(
            new VoxSettings { FirstRunCompleted = false, SpeechRateWpm = 200 });

        var wizardTask = wizard.RunAsync();
        await PressAsync(wizard, wizardTask, hook, 0x0D); // begin
        await PressAsync(wizard, wizardTask, hook, 0x26); // rate up
        await PressAsync(wizard, wizardTask, hook, 0x0D); // accept rate
        await PressAsync(wizard, wizardTask, hook, 0x1B); // Escape at the voice step

        await wizardTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(monitor.CurrentValue.FirstRunCompleted);
        Assert.Equal(210, monitor.CurrentValue.SpeechRateWpm);
    }

    [Fact]
    public async Task RunAsync_NoKeyForAWhile_EndsWizard()
    {
        var (wizard, hook, _, monitor, _) = CreateWizard(new VoxSettings { FirstRunCompleted = false });
        wizard.InactivityTimeout = TimeSpan.FromMilliseconds(300);

        var wizardTask = wizard.RunAsync();
        await PressAsync(wizard, wizardTask, hook, 0x0D); // begin, then walk away

        await wizardTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(monitor.CurrentValue.FirstRunCompleted);
    }

    [Fact]
    public async Task RunAsync_WizardKeys_AreOnlySwallowedWhileWaiting()
    {
        var (wizard, hook, engine, _, _) = CreateWizard(new VoxSettings { FirstRunCompleted = false });
        // The confirmation after the rate step speaks for a while: no key is awaited then
        var confirming = new TaskCompletionSource();
        var confirmationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine
            .Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Returns((Utterance u, CancellationToken _) =>
            {
                if (!u.Text.StartsWith("Speech rate set"))
                    return Task.CompletedTask;
                confirmationStarted.TrySetResult();
                return confirming.Task;
            });

        var wizardTask = wizard.RunAsync();
        await WaitUntilWaitingAsync(wizard, wizardTask);
        Assert.True(hook.SuppressionFilter!(new KeyEvent { VkCode = 0x0D, IsKeyDown = true }).Suppress); // waiting
        await PressAsync(wizard, wizardTask, hook, 0x0D); // begin
        await PressAsync(wizard, wizardTask, hook, 0x0D); // accept rate -> confirmation speaking, nothing awaited
        await confirmationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(wizard.IsWaitingForKey);
        Assert.False(hook.SuppressionFilter!(new KeyEvent { VkCode = 0x0D, IsKeyDown = true }).Suppress);

        confirming.SetResult();
        await PressAsync(wizard, wizardTask, hook, 0x1B);
        await wizardTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // -------------------------------------------------------------------------
    // Round 6: current voice and early exit
    // -------------------------------------------------------------------------

    [Fact]
    public async Task VoiceStep_StartsAtTheVoiceSpeaking_AndEnterKeepsIt()
    {
        var (wizard, hook, engine, monitor, _) = CreateWizard(new VoxSettings { FirstRunCompleted = false });
        var spoken = new List<string>();
        engine.Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Returns((Utterance u, CancellationToken _) => { lock (spoken) spoken.Add(u.Text); return Task.CompletedTask; });
        engine.SetupGet(e => e.CurrentVoice).Returns("Voice Two");

        var wizardTask = wizard.RunAsync();
        foreach (var vk in new[] { 0x0D, 0x0D, 0x0D }) // welcome, rate, voice (keep)
        {
            await PressAsync(wizard, wizardTask, hook, vk);
        }
        await PressAsync(wizard, wizardTask, hook, 0x1B); // leave the wizard
        await wizardTask.WaitAsync(TimeSpan.FromSeconds(5));

        lock (spoken)
            Assert.Contains(spoken, t => t.Contains("Current voice: Voice Two"));
        engine.Verify(e => e.SetVoice(It.Is<string>(v => v.StartsWith("Voice"))), Times.Never);
        Assert.Null(monitor.CurrentValue.VoiceName);
    }

    [Fact]
    public async Task EscapeWhilePreviewingRate_RestoresTheSavedRate()
    {
        var (wizard, hook, engine, monitor, _) = CreateWizard(
            new VoxSettings { FirstRunCompleted = false, SpeechRateWpm = 200 });
        var rates = new List<int>();
        engine.Setup(e => e.SetRate(It.IsAny<int>())).Callback((int r) => { lock (rates) rates.Add(r); });

        var wizardTask = wizard.RunAsync();
        foreach (var vk in new[] { 0x0D, 0x26, 0x1B }) // welcome, Up (preview 210), Escape
        {
            await PressAsync(wizard, wizardTask, hook, vk);
        }
        await wizardTask.WaitAsync(TimeSpan.FromSeconds(5));

        lock (rates)
        {
            Assert.Contains(210, rates);
            Assert.Equal(200, rates[^1]);
        }
        Assert.Equal(200, monitor.CurrentValue.SpeechRateWpm);
    }

    // -------------------------------------------------------------------------
    // Bringing over NVDA's or JAWS's settings
    // -------------------------------------------------------------------------

    private sealed class FakeImportOffer(SettingsMonitor monitor, params ImportSource[] sources) : ISettingsImportOffer
    {
        public List<ImportSource> Imported { get; } = new();
        public List<ImportReport> Shown { get; } = new();

        public IReadOnlyList<ImportSource> FindSources() => sources;

        public ImportReport? Import(ImportSource source)
        {
            Imported.Add(source);
            monitor.UpdateSettings(monitor.CurrentValue with { SpeechRateWpm = 300 });
            var report = new ImportReport(source);
            report.AddImported("rate", nameof(VoxSettings.SpeechRateWpm), 300, 180);
            return report;
        }

        public void ShowReport(ImportReport report) => Shown.Add(report);
    }

    [Fact]
    public async Task RunAsync_SettingsFound_TwoBringsOverJaws_AndTheReportOpensAtTheEnd()
    {
        var (wizard, hook, engine, monitor, manager) = CreateWizard(new VoxSettings { FirstRunCompleted = false });
        var offer = new FakeImportOffer(monitor, ImportSource.Nvda, ImportSource.Jaws);
        wizard = new FirstRunWizard(engine.Object, manager, monitor, hook, NullLogger<FirstRunWizard>.Instance, importOffer: offer);

        var wizardTask = wizard.RunAsync();
        await PressAsync(wizard, wizardTask, hook, 0x0D); // Enter: begin
        await PressAsync(wizard, wizardTask, hook, 0x32); // 2: JAWS
        await WaitUntilWaitingAsync(wizard, wizardTask);
        Assert.Equal([ImportSource.Jaws], offer.Imported);
        Assert.Equal(300, monitor.CurrentValue.SpeechRateWpm);
        Assert.Empty(offer.Shown);

        for (int i = 0; i < 10 && !wizardTask.IsCompleted; i++)
            await PressAsync(wizard, wizardTask, hook, 0x0D);
        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));

        // The rate step started from the imported rate and Enter kept it
        Assert.Equal(300, monitor.CurrentValue.SpeechRateWpm);
        Assert.Single(offer.Shown);
    }

    [Fact]
    public async Task RunAsync_SettingsFound_EnterSetsUpVoxWithoutImporting()
    {
        var (wizard, hook, engine, monitor, manager) = CreateWizard(new VoxSettings { FirstRunCompleted = false });
        var offer = new FakeImportOffer(monitor, ImportSource.Nvda);
        wizard = new FirstRunWizard(engine.Object, manager, monitor, hook, NullLogger<FirstRunWizard>.Instance, importOffer: offer);

        var wizardTask = wizard.RunAsync();
        for (int i = 0; i < 12 && !wizardTask.IsCompleted; i++)
            await PressAsync(wizard, wizardTask, hook, 0x0D);
        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(offer.Imported);
        Assert.Empty(offer.Shown);
        Assert.True(monitor.CurrentValue.FirstRunCompleted);
    }

    // -------------------------------------------------------------------------
    // Trial mode
    // -------------------------------------------------------------------------

    private sealed class FakeTrialOffer(Vox.Core.Trial.RunningScreenReader? running) : Vox.Core.Trial.ITrialOffer
    {
        public int Begun { get; private set; }
        public Vox.Core.Trial.RunningScreenReader? FindRunning() => Begun > 0 ? null : running;
        public string Begin()
        {
            Begun++;
            return "Trial mode.";
        }
        public string ReturnKey => "Insert+Ctrl+F4";
    }

    [Fact]
    public async Task RunAsync_AnotherScreenReaderRunning_OneBeginsTheTrial()
    {
        var (wizard, hook, engine, monitor, manager) = CreateWizard(new VoxSettings { FirstRunCompleted = false });
        var offer = new FakeTrialOffer(new Vox.Core.Trial.RunningScreenReader(Vox.Core.Trial.OtherScreenReader.Nvda));
        var spoken = new List<string>();
        engine.Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Callback<Utterance, CancellationToken>((u, _) => { lock (spoken) spoken.Add(u.Text); })
            .Returns(Task.CompletedTask);
        wizard = new FirstRunWizard(engine.Object, manager, monitor, hook, NullLogger<FirstRunWizard>.Instance, trialOffer: offer);

        var wizardTask = wizard.RunAsync();
        await PressAsync(wizard, wizardTask, hook, 0x0D); // Enter: begin
        await PressAsync(wizard, wizardTask, hook, 0x31); // 1: trial
        await WaitUntilWaitingAsync(wizard, wizardTask);
        Assert.Equal(1, offer.Begun);
        lock (spoken)
        {
            Assert.Contains(spoken, t => t.StartsWith("NVDA is running") && t.Contains("Insert+Ctrl+F4"));
            Assert.Contains("Trial mode.", spoken);
        }

        for (int i = 0; i < 10 && !wizardTask.IsCompleted; i++)
            await PressAsync(wizard, wizardTask, hook, 0x0D);
        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(monitor.CurrentValue.FirstRunCompleted);
    }

    [Fact]
    public async Task RunAsync_AnotherScreenReaderRunning_EnterGoesOnWithoutATrial()
    {
        var (wizard, hook, engine, monitor, manager) = CreateWizard(new VoxSettings { FirstRunCompleted = false });
        var offer = new FakeTrialOffer(new Vox.Core.Trial.RunningScreenReader(Vox.Core.Trial.OtherScreenReader.Narrator));
        wizard = new FirstRunWizard(engine.Object, manager, monitor, hook, NullLogger<FirstRunWizard>.Instance, trialOffer: offer);

        var wizardTask = wizard.RunAsync();
        for (int i = 0; i < 12 && !wizardTask.IsCompleted; i++)
            await PressAsync(wizard, wizardTask, hook, 0x0D);
        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, offer.Begun);
        Assert.True(monitor.CurrentValue.FirstRunCompleted);
    }

    // -------------------------------------------------------------------------
    // Magnifier
    // -------------------------------------------------------------------------

    private sealed class FakeMagnifierOffer(string? refusal = null) : Vox.Core.Magnifier.IMagnifierOffer
    {
        public int TurnedOn { get; private set; }
        public string ToggleKey => "Insert+Alt+Z";
        public string? TurnOn()
        {
            TurnedOn++;
            return refusal;
        }
    }

    [Fact]
    public async Task RunAsync_OneAtTheMagnifierQuestion_TurnsItOnAndKeepsIt()
    {
        var (wizard, hook, engine, monitor, manager) = CreateWizard(new VoxSettings { FirstRunCompleted = false });
        var offer = new FakeMagnifierOffer();
        var spoken = new List<string>();
        engine.Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Callback<Utterance, CancellationToken>((u, _) => { lock (spoken) spoken.Add(u.Text); })
            .Returns(Task.CompletedTask);
        wizard = new FirstRunWizard(engine.Object, manager, monitor, hook, NullLogger<FirstRunWizard>.Instance, magnifierOffer: offer);

        var wizardTask = wizard.RunAsync();
        // Welcome, rate, voice, verbosity, layout, modifier
        for (int i = 0; i < 6; i++)
            await PressAsync(wizard, wizardTask, hook, 0x0D);
        await PressAsync(wizard, wizardTask, hook, 0x31); // 1: magnifier on
        await WaitUntilWaitingAsync(wizard, wizardTask);
        Assert.Equal(1, offer.TurnedOn);
        lock (spoken)
            Assert.Contains(spoken, t => t.StartsWith("Do you use magnification?") && t.Contains("Insert+Alt+Z"));

        for (int i = 0; i < 10 && !wizardTask.IsCompleted; i++)
            await PressAsync(wizard, wizardTask, hook, 0x0D);
        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(monitor.CurrentValue.MagnifierEnabled);
        Assert.True(monitor.CurrentValue.FirstRunCompleted);
    }

    [Fact]
    public async Task RunAsync_MagnifierCannotStart_SaysWhyAndLeavesItOff()
    {
        var (wizard, hook, engine, monitor, manager) = CreateWizard(new VoxSettings { FirstRunCompleted = false });
        var offer = new FakeMagnifierOffer("Windows Magnifier is running.");
        var spoken = new List<string>();
        engine.Setup(e => e.SpeakAsync(It.IsAny<Utterance>(), It.IsAny<CancellationToken>()))
            .Callback<Utterance, CancellationToken>((u, _) => { lock (spoken) spoken.Add(u.Text); })
            .Returns(Task.CompletedTask);
        wizard = new FirstRunWizard(engine.Object, manager, monitor, hook, NullLogger<FirstRunWizard>.Instance, magnifierOffer: offer);

        var wizardTask = wizard.RunAsync();
        for (int i = 0; i < 6; i++)
            await PressAsync(wizard, wizardTask, hook, 0x0D);
        await PressAsync(wizard, wizardTask, hook, 0x31);
        await WaitUntilWaitingAsync(wizard, wizardTask);
        lock (spoken)
            Assert.Contains("Windows Magnifier is running.", spoken);

        for (int i = 0; i < 10 && !wizardTask.IsCompleted; i++)
            await PressAsync(wizard, wizardTask, hook, 0x0D);
        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(monitor.CurrentValue.MagnifierEnabled);
    }

    [Fact]
    public async Task RunAsync_EnterAtTheMagnifierQuestion_GoesOnWithoutIt()
    {
        var (wizard, hook, engine, monitor, manager) = CreateWizard(new VoxSettings { FirstRunCompleted = false });
        var offer = new FakeMagnifierOffer();
        wizard = new FirstRunWizard(engine.Object, manager, monitor, hook, NullLogger<FirstRunWizard>.Instance, magnifierOffer: offer);

        var wizardTask = wizard.RunAsync();
        for (int i = 0; i < 14 && !wizardTask.IsCompleted; i++)
            await PressAsync(wizard, wizardTask, hook, 0x0D);
        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, offer.TurnedOn);
        Assert.False(monitor.CurrentValue.MagnifierEnabled);
        Assert.True(monitor.CurrentValue.FirstRunCompleted);
    }
}
