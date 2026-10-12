using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Accessibility;
using Vox.Core.AppModules;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.AppModules;

public class AppModuleManagerTests
{
    private sealed class FakeForeground : IForegroundWindow
    {
        public ForegroundWindowInfo? Window { get; set; }
        public ForegroundWindowInfo? Get() => Window;
    }

    private sealed class SinkStub : IEventSink
    {
        public List<ScreenReaderEvent> Posted { get; } = [];
        public void Post(ScreenReaderEvent evt) => Posted.Add(evt);
    }

    private sealed class TestModule(string id, params string[] processes) : AppModule
    {
        public override string Id => id;
        public override string Name => id;
        public override IReadOnlyCollection<string> ProcessNames => processes;

        public List<string> Log { get; } = [];
        public List<KeyBinding> Keys { get; } = [];
        public Func<FocusChangedEvent, string, string?>? Formatter { get; set; }
        public Func<ScreenReaderEvent, bool>? Filter { get; set; }
        public NavigationCommand? Handles { get; set; }
        public bool Throw { get; set; }

        public override IReadOnlyList<KeyBinding> KeyBindings => Keys;

        public override void OnActivated(AppActivation activation)
        {
            base.OnActivated(activation);
            Log.Add($"activated {activation.ProcessName} {activation.ProcessId}");
        }

        public override void OnDeactivated()
        {
            base.OnDeactivated();
            Log.Add("deactivated");
        }

        public override bool TryHandle(NavigationCommand command)
        {
            if (Throw)
                throw new InvalidOperationException("broken");
            return command == Handles;
        }

        public override string? FormatFocusAnnouncement(FocusChangedEvent focus, string announcement)
        {
            if (Throw)
                throw new InvalidOperationException("broken");
            return Formatter is null ? announcement : Formatter(focus, announcement);
        }

        public override bool ShouldProcess(ScreenReaderEvent evt) => Filter?.Invoke(evt) ?? true;

        public override void OnEvent(ScreenReaderEvent evt) => Log.Add($"event {evt.GetType().Name}");
    }

    private readonly FakeForeground _foreground = new();
    private readonly Dictionary<int, string> _processes = new() { [10] = "WINWORD", [20] = "EXCEL", [30] = "notepad", [11] = "WINWORD" };
    private readonly TestOptionsMonitor<VoxSettings> _settings = new(new VoxSettings());
    private KeyMap? _appKeyMap;
    private int _keyMapSets;

    private AppModuleManager Create(params IAppModule[] modules) =>
        new(modules, _foreground, map => { _appKeyMap = map; _keyMapSets++; }, _settings,
            NullLogger<AppModuleManager>.Instance, pid => _processes.GetValueOrDefault(pid));

    private void Foreground(int pid) => _foreground.Window = new ForegroundWindowInfo(new IntPtr(pid * 100), "Window", pid);

    private static FocusChangedEvent Focus(string name = "Item") => new(DateTimeOffset.UtcNow, name, "Edit");

    [Fact]
    public void Refresh_ActivatesTheModuleForTheForegroundProcess_ByNameInAnyCase()
    {
        var word = new TestModule("word", "winword");
        var excel = new TestModule("excel", "EXCEL");
        var manager = Create(word, excel);

        Foreground(10);
        manager.Refresh();

        Assert.Same(word, manager.ActiveModule);
        Assert.Equal(["activated WINWORD 10"], word.Log);
        Assert.Empty(excel.Log);
        Assert.Equal("WINWORD", manager.ActiveProcessName);
    }

    [Fact]
    public void Refresh_SwitchingApplications_DeactivatesTheOldModuleFirst()
    {
        var word = new TestModule("word", "WINWORD");
        var excel = new TestModule("excel", "EXCEL");
        var manager = Create(word, excel);

        Foreground(10);
        manager.Refresh();
        Foreground(20);
        manager.Refresh();

        Assert.Same(excel, manager.ActiveModule);
        Assert.Equal(["activated WINWORD 10", "deactivated"], word.Log);
        Assert.Equal(["activated EXCEL 20"], excel.Log);
    }

    [Fact]
    public void Refresh_ApplicationWithoutAModule_LeavesNoneActive()
    {
        var word = new TestModule("word", "WINWORD");
        var manager = Create(word);

        Foreground(10);
        manager.Refresh();
        Foreground(30);
        manager.Refresh();

        Assert.Null(manager.ActiveModule);
        Assert.Equal("notepad", manager.ActiveProcessName);
        Assert.Null(_appKeyMap);
    }

    [Fact]
    public void Refresh_SameProcessAgain_DoesNothing()
    {
        var word = new TestModule("word", "WINWORD");
        var manager = Create(word);
        var changes = new List<string>();
        manager.ForegroundAppChanged += (_, name) => changes.Add(name ?? "(none)");

        Foreground(10);
        manager.Refresh();
        manager.Refresh();
        manager.Refresh();

        Assert.Equal(["activated WINWORD 10"], word.Log);
        Assert.Equal(["WINWORD"], changes);
    }

    [Fact]
    public void Refresh_AnotherInstanceOfTheSameApplication_StartsTheModuleAgainOnIt()
    {
        var word = new TestModule("word", "WINWORD");
        var manager = Create(word);

        Foreground(10);
        manager.Refresh();
        Foreground(11);
        manager.Refresh();

        Assert.Equal(["activated WINWORD 10", "deactivated", "activated WINWORD 11"], word.Log);
    }

    [Fact]
    public void Refresh_NoForegroundWindow_KeepsTheModule()
    {
        var word = new TestModule("word", "WINWORD");
        var manager = Create(word);
        Foreground(10);
        manager.Refresh();

        _foreground.Window = null;
        manager.Refresh();

        Assert.Same(word, manager.ActiveModule);
    }

    [Fact]
    public void DisabledModule_IsNotActivated_AndReselectPicksItUpWhenTurnedOn()
    {
        var word = new TestModule("word", "WINWORD");
        _settings.Set(new VoxSettings { DisabledAppModules = ["Word"] });
        var manager = Create(word);

        Foreground(10);
        manager.Refresh();
        Assert.Null(manager.ActiveModule);

        _settings.Set(new VoxSettings());
        manager.Reselect();
        Assert.Same(word, manager.ActiveModule);

        _settings.Set(new VoxSettings { DisabledAppModules = ["word"] });
        manager.Reselect();
        Assert.Null(manager.ActiveModule);
        Assert.Equal(["activated WINWORD 10", "deactivated"], word.Log);
    }

    [Fact]
    public void KeyLayer_IsTheActiveModulesKeys_AndClearedWhenNoneIsActive()
    {
        var word = new TestModule("word", "WINWORD");
        word.Keys.Add(new KeyBinding(KeyModifiers.Insert, 0x71, "Any", nameof(NavigationCommand.PageSummary)));
        var excel = new TestModule("excel", "EXCEL");
        var manager = Create(word, excel);

        Foreground(10);
        manager.Refresh();
        Assert.NotNull(_appKeyMap);
        Assert.True(_appKeyMap!.TryResolveOutsideDocument(KeyModifiers.Insert, 0x71, out var command, out _));
        Assert.Equal(NavigationCommand.PageSummary, command);

        // A module without keys has no layer
        Foreground(20);
        manager.Refresh();
        Assert.Null(_appKeyMap);

        manager.Stop();
        Assert.Null(_appKeyMap);
        Assert.Null(manager.ActiveModule);
    }

    [Fact]
    public void KeyLayer_ThroughTheDispatcher_IsCheckedBeforeTheKeymapOnlyWhileTheModuleIsActive()
    {
        var hook = new Mock<IKeyboardHook>();
        var keyMap = KeyMap.LoadFromJson("""
            { "bindings": [ { "modifiers": "Ctrl|Shift", "vkCode": 70, "mode": "Any", "command": "SayAll" } ] }
            """);
        var dispatcher = new KeyInputDispatcher(hook.Object, keyMap, new SinkStub(), NullLogger<KeyInputDispatcher>.Instance);
        var word = new TestModule("word", "WINWORD");
        word.Keys.Add(new KeyBinding(KeyModifiers.Ctrl | KeyModifiers.Shift, 70, "Any", nameof(NavigationCommand.PageSummary)));
        word.Keys.Add(new KeyBinding(KeyModifiers.Ctrl | KeyModifiers.Shift, 71, "Any", nameof(NavigationCommand.PageSummary), PassThrough: true));
        var manager = new AppModuleManager([word], _foreground, dispatcher.SetAppKeyMap, _settings,
            processName: pid => _processes.GetValueOrDefault(pid));
        var ctrlShiftF = new KeyEvent { VkCode = 70, Modifiers = KeyModifiers.Ctrl | KeyModifiers.Shift, IsKeyDown = true };
        var ctrlShiftG = ctrlShiftF with { VkCode = 71 };

        Assert.False(dispatcher.ShouldSuppress(ctrlShiftG));
        Foreground(10);
        manager.Refresh();
        Assert.True(dispatcher.ShouldSuppress(ctrlShiftF));
        // A pass-through binding still lets the key through
        Assert.False(dispatcher.ShouldSuppress(ctrlShiftG));

        Foreground(30);
        manager.Refresh();
        Assert.False(dispatcher.ShouldSuppress(ctrlShiftG));
        // The keymap's own binding is back
        Assert.True(dispatcher.ShouldSuppress(ctrlShiftF));
    }

    [Fact]
    public void Dispatcher_PostsTheModulesCommand_ForItsKey()
    {
        EventHandler<KeyEvent>? handler = null;
        var hook = new Mock<IKeyboardHook>();
        hook.SetupAdd(h => h.KeyPressed += It.IsAny<EventHandler<KeyEvent>>()).Callback<EventHandler<KeyEvent>>(h => handler = h);
        var sink = new SinkStub();
        var keyMap = KeyMap.LoadFromJson("""
            { "bindings": [ { "modifiers": "Ctrl|Shift", "vkCode": 70, "mode": "Any", "command": "SayAll" } ] }
            """);
        var dispatcher = new KeyInputDispatcher(hook.Object, keyMap, sink, NullLogger<KeyInputDispatcher>.Instance);
        dispatcher.Start();
        dispatcher.SetAppKeyMap(KeyMap.FromBindings(
            [new KeyBinding(KeyModifiers.Ctrl | KeyModifiers.Shift, 70, "Any", nameof(NavigationCommand.PageSummary))], out var warnings));

        handler!(hook.Object, new KeyEvent { VkCode = 70, Modifiers = KeyModifiers.Ctrl | KeyModifiers.Shift, IsKeyDown = true });

        Assert.Empty(warnings);
        var command = Assert.IsType<NavigationCommandEvent>(Assert.Single(sink.Posted));
        Assert.Equal(NavigationCommand.PageSummary, command.Command);
    }

    [Fact]
    public void FromBindings_ReportsUnknownCommands()
    {
        KeyMap.FromBindings([new KeyBinding(KeyModifiers.None, 70, "Any", "NoSuchCommand")], out var warnings);

        Assert.Contains("NoSuchCommand", Assert.Single(warnings));
    }

    [Fact]
    public void Calls_GoToTheActiveModuleOnly()
    {
        var word = new TestModule("word", "WINWORD")
        {
            Handles = NavigationCommand.PageSummary,
            Formatter = (_, text) => $"Word {text}",
            Filter = evt => evt is not NotificationEvent,
        };
        var manager = Create(word);
        var notification = new NotificationEvent(DateTimeOffset.UtcNow, "a", "text");

        // Nothing active: everything as Vox does it
        Assert.False(manager.TryHandle(NavigationCommand.PageSummary));
        Assert.Equal("Item, edit", manager.FormatFocusAnnouncement(Focus(), "Item, edit"));
        Assert.True(manager.ShouldProcess(notification));
        manager.OnEvent(notification);
        Assert.Empty(word.Log);

        Foreground(10);
        manager.Refresh();
        Assert.True(manager.TryHandle(NavigationCommand.PageSummary));
        Assert.False(manager.TryHandle(NavigationCommand.SayAll));
        Assert.Equal("Word Item, edit", manager.FormatFocusAnnouncement(Focus(), "Item, edit"));
        Assert.False(manager.ShouldProcess(notification));
        manager.OnEvent(notification);
        Assert.Contains("event NotificationEvent", word.Log);
    }

    [Fact]
    public void AFailingModule_DoesNotBreakVox()
    {
        var word = new TestModule("word", "WINWORD") { Throw = true };
        var manager = Create(word);
        Foreground(10);
        manager.Refresh();

        // Its own command is taken as handled (nothing else runs it); the announcement is Vox's own
        Assert.True(manager.TryHandle(NavigationCommand.PageSummary));
        Assert.Equal("Item, edit", manager.FormatFocusAnnouncement(Focus(), "Item, edit"));
    }

    [Fact]
    public void ModuleKeyMapFiles_AllParse_WithKnownCommands()
    {
        foreach (var id in AppModuleKeyMaps.ModuleIds())
        {
            var bindings = AppModuleKeyMaps.Load(id);
            Assert.NotEmpty(bindings);
            KeyMap.FromBindings(bindings, out var warnings);
            Assert.True(warnings.Count == 0, $"{id}: {string.Join("; ", warnings)}");
            foreach (var binding in bindings)
                Assert.True(Enum.TryParse<NavigationCommand>(binding.Command, out var command) && CommandCatalog.Has(command),
                    $"{id}: {binding.Command} needs a CommandCatalog entry");
        }
    }

    [Fact]
    public void AppModuleKeyMaps_UnknownModule_HasNoKeys() =>
        Assert.Empty(AppModuleKeyMaps.Load("no-such-module"));
}
