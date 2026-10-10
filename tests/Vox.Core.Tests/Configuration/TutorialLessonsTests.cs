using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Configuration;

public sealed class TutorialLessonsTests : IDisposable
{
    private static readonly string ConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "config");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VoxLessons_" + Guid.NewGuid().ToString("N"));

    public TutorialLessonsTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    private sealed class FakeHook : IKeyboardHook
    {
        public event EventHandler<KeyEvent>? KeyPressed;
        public Func<KeyEvent, KeyDecision>? SuppressionFilter { get; set; }
        public ModifierKey ScreenReaderModifier { get; set; }
        public void Install() { }
        public void Uninstall() { }

        public KeyDecision Press(int vk, KeyModifiers modifiers = KeyModifiers.None)
        {
            var key = new KeyEvent { VkCode = vk, Modifiers = modifiers, IsKeyDown = true };
            var decision = SuppressionFilter?.Invoke(key) ?? KeyDecision.Pass;
            KeyPressed?.Invoke(this, key);
            return decision;
        }
    }

    [Theory]
    [InlineData(KeyboardLayout.Desktop)]
    [InlineData(KeyboardLayout.Laptop)]
    public void EveryLessonStepHasAKey_InBothLayouts(KeyboardLayout layout)
    {
        var keys = new GestureEditor(KeyMap.LayoutBindings(ConfigDirectory, layout));

        foreach (var step in TutorialLessons.All.SelectMany(l => l.Steps))
            Assert.True(TutorialLessons.KeyFor(step.Command, keys) is not null, $"{step.Command} has no key in the {layout} layout");
    }

    [Fact]
    public void Matches_TakesKeypadKeysAsTheirKeypadCodes()
    {
        var numpad8 = new Gesture(KeyModifiers.None, 104, "Any");

        // Num Lock off: keypad 8 arrives as Up Arrow, marked as a keypad key
        Assert.True(TutorialLessons.Matches(new KeyEvent { VkCode = 0x26, IsKeypad = true, IsKeyDown = true }, numpad8));
        Assert.False(TutorialLessons.Matches(new KeyEvent { VkCode = 0x26, IsKeyDown = true }, numpad8));
        Assert.False(TutorialLessons.Matches(new KeyEvent { VkCode = 104, Modifiers = KeyModifiers.Insert, IsKeyDown = true }, numpad8));
    }

    [Fact]
    public async Task Lesson_ConfirmsTheRightKey_CorrectsAWrongOne_AndSwallowsKeysWhileWaiting()
    {
        var speech = new RecordingSpeechEngine();
        var hook = new FakeHook();
        var manager = new SettingsManager(NullLogger<SettingsManager>.Instance,
            Path.Combine(_directory, "defaults.json"), Path.Combine(_directory, "settings.json"));
        using var monitor = new SettingsMonitor(manager, NullLogger<SettingsMonitor>.Instance);
        var wizard = new FirstRunWizard(speech, manager, monitor, hook, NullLogger<FirstRunWizard>.Instance,
            () => new GestureEditor(KeyMap.LayoutBindings(ConfigDirectory, KeyboardLayout.Desktop)));

        var lessons = wizard.RunLessonsAsync();
        async Task Press(int vk, KeyModifiers modifiers = KeyModifiers.None)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!wizard.IsWaitingForKey && DateTime.UtcNow < deadline)
                await Task.Delay(5);
            Assert.True(hook.Press(vk, modifiers).Suppress);
        }

        await Press(0x31);                 // 1: web pages
        await speech.WaitForAsync(s => s.Text.Contains("press H"), TimeSpan.FromSeconds(5));
        await Press(0x4B);                 // K: not the heading key
        await speech.WaitForAsync(s => s.Text.StartsWith("That was K."), TimeSpan.FromSeconds(5));
        await Press(0x48);                 // H
        await speech.WaitForAsync(s => s.Text.StartsWith("Right. That moves to the next heading"), TimeSpan.FromSeconds(5));
        await Press(0x1B);                 // Escape: leave the lesson
        await Press(0x1B);                 // Escape: leave the lessons
        await lessons.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains(speech.SpokenText, t => t.StartsWith("Practice finished"));
        Assert.Null(hook.SuppressionFilter);
    }
}
