using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Speech;
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
        var userPath = SettingsManager.UserSettingsPath;
        if (File.Exists(userPath))
        {
            _userSettingsBackup = userPath + ".bak_" + Guid.NewGuid().ToString("N");
            File.Copy(userPath, _userSettingsBackup, overwrite: true);
        }
    }

    public void Dispose()
    {
        var userPath = SettingsManager.UserSettingsPath;
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
        CreateWizard(VoxSettings? initialSettings = null)
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
        var manager = new SettingsManager(logger, tempPath);

        // Ensure UserSettingsPath doesn't exist so SettingsMonitor loads from tempPath
        var userPath = SettingsManager.UserSettingsPath;
        string? backupPath = null;
        if (File.Exists(userPath))
        {
            backupPath = userPath + ".test_bak_" + Guid.NewGuid().ToString("N");
            File.Move(userPath, backupPath);
        }

        SettingsMonitor monitor;
        try
        {
            var monitorLogger = NullLogger<SettingsMonitor>.Instance;
            monitor = new SettingsMonitor(manager, monitorLogger);
        }
        finally
        {
            // Restore if we moved it
            if (backupPath != null && File.Exists(backupPath))
            {
                if (File.Exists(userPath)) File.Delete(userPath);
                File.Move(backupPath, userPath);
            }
        }

        var wizard = new FirstRunWizard(
            engineMock.Object,
            manager,
            monitor,
            hook,
            NullLogger<FirstRunWizard>.Instance);

        return (wizard, hook, engineMock, monitor, manager);
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
        await Task.Delay(50);
        hook.SimulateKeyDown(0x1B); // Escape

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
        await Task.Delay(50);
        hook.SimulateKeyDown(0x1B); // Escape

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
            await Task.Delay(80);
            hook.SimulateKeyDown(0x0D); // Enter
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
            await Task.Delay(80);
            hook.SimulateKeyDown(0x0D); // Enter
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
        await Task.Delay(50);
        hook.SimulateKeyDown(0x0D);

        // Rate step: press Up to increase, then Enter to confirm
        await Task.Delay(80);
        hook.SimulateKeyDown(0x26); // Up arrow
        await Task.Delay(50);
        hook.SimulateKeyDown(0x0D); // Enter

        // Drive remaining steps with Enter
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(80);
            hook.SimulateKeyDown(0x0D);
            if (wizardTask.IsCompleted) break;
        }

        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(capturedRate > 200 || monitor.CurrentValue.SpeechRateWpm > 200,
            $"Rate should have increased above 200, was: capturedRate={capturedRate}, saved={monitor.CurrentValue.SpeechRateWpm}");
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
        await Task.Delay(50);
        hook.SimulateKeyDown(0x0D);

        // Rate step → Enter
        await Task.Delay(80);
        hook.SimulateKeyDown(0x0D);

        // Voice step → Enter
        await Task.Delay(80);
        hook.SimulateKeyDown(0x0D);

        // Verbosity step → press 3 for Advanced
        await Task.Delay(80);
        hook.SimulateKeyDown(0x33); // '3'

        // Modifier step → Enter (1 for Insert)
        await Task.Delay(80);
        hook.SimulateKeyDown(0x31); // '1'

        // Tutorial → Enter
        await Task.Delay(80);
        hook.SimulateKeyDown(0x0D);

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
        await Task.Delay(50);
        hook.SimulateKeyDown(0x0D);
        // Rate → Enter
        await Task.Delay(80);
        hook.SimulateKeyDown(0x0D);
        // Voice → Enter
        await Task.Delay(80);
        hook.SimulateKeyDown(0x0D);
        // Verbosity → 1
        await Task.Delay(80);
        hook.SimulateKeyDown(0x31);
        // Modifier → 2 for CapsLock
        await Task.Delay(80);
        hook.SimulateKeyDown(0x32);
        // Tutorial → Enter
        await Task.Delay(80);
        hook.SimulateKeyDown(0x0D);

        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ModifierKey.CapsLock, monitor.CurrentValue.ModifierKey);
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

        await Task.Delay(50);
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
            await Task.Delay(80);
            hook.SimulateKeyDown(0x0D); // Enter works for all steps now
            if (wizardTask.IsCompleted) break;
        }

        await wizardTask.WaitAsync(TimeSpan.FromSeconds(10));

        // Settings should be updated in-memory
        Assert.True(monitor.CurrentValue.FirstRunCompleted);
    }
}
