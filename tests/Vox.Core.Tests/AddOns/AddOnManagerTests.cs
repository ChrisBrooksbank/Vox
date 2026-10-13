using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.AddOns;
using Vox.Core.Braille;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Vox.Sdk;
using Xunit;
using CoreAppModule = Vox.Core.AppModules.IAppModule;

namespace Vox.Core.Tests.AddOns;

public class AddOnManagerTests : IDisposable
{
    private readonly AddOnTestFiles _files = new();
    private readonly FakeLoader _loader = new();
    private readonly List<(string Text, Sdk.SpeechPriority Priority)> _said = [];
    private AddOnKeyLayer? _keys;
    private IReadOnlyList<CoreAppModule> _modules = [];
    private CoreAppModule? _active;

    public void Dispose() => _files.Dispose();

    private AddOnIntegration Integration(SpeechEngineRegistry? engines = null, BrailleDisplayManager? braille = null, string? selectedEngine = null) => new()
    {
        Speak = (text, priority) => _said.Add((text, priority)),
        SetKeyLayer = layer => _keys = layer,
        SetAppModules = modules => _modules = modules,
        ActiveAppModule = () => _active,
        SpeechEngines = engines,
        SelectedSpeechEngine = () => selectedEngine,
        Braille = braille,
    };

    private AddOnManager Manager(RunPolicy? policy = null, AddOnIntegration? integration = null, TimeProvider? time = null) =>
        new(_files.AddOnsDirectory, policy ?? RunPolicy.Normal, _loader, AddOnTrustStore.Empty, integration ?? Integration(), time: time);

    /// <summary>Installs an add-on's files through the manager, as the add-ons dialog does.</summary>
    private void Install(AddOnManager manager, string id = "com.example.test", string version = "1.0.0", string minApi = "1.0")
    {
        var check = manager.CheckPackage(_files.Package(_files.Source(id, version, minApi)));
        manager.InstallFiles(check);
    }

    private static AddOnCommand Command(string id, Action run, string gesture = "VoxKey+Ctrl+F12") =>
        new(id, "Command " + id, "Does " + id, run) { Gestures = [KeyGesture.Parse(gesture)] };

    [Fact]
    public void AnInstalledAddOn_IsLoaded_AndItsCommandRunsFromItsKey()
    {
        int runs = 0;
        _loader.Set("com.example.test", () => new FakeAddOn(host => host.AddCommand(Command("hello", () => runs++))));
        var manager = Manager();
        Install(manager);

        manager.LoadAll();

        var info = Assert.Single(manager.List());
        Assert.Equal(AddOnState.Running, info.State);
        Assert.Equal("Command hello", Assert.Single(info.Commands).Name);
        Assert.NotNull(_keys);
        Assert.True(_keys!.TryResolveOutsideDocument(KeyModifiers.Insert | KeyModifiers.Ctrl, 0x7B, out var command));
        manager.RunCommand(command);
        Assert.Equal(1, runs);
    }

    [Fact]
    public void Disable_UnloadsTheAddOn_AndItStaysOffNextTime()
    {
        var manager = Manager();
        Install(manager);
        manager.LoadAll();
        var loaded = Assert.Single(_loader.Loaded);

        Assert.True(manager.Disable("com.example.test"));

        Assert.True(loaded.Unloaded);
        Assert.True(((FakeAddOn)loaded.Instance).Disposed);
        Assert.Equal(AddOnState.Disabled, Assert.Single(manager.List()).State);
        var next = Manager();
        next.LoadAll();
        Assert.Single(_loader.Loaded);
        Assert.Equal(AddOnState.Disabled, Assert.Single(next.List()).State);
    }

    [Fact]
    public void Enable_LoadsItAgainAtOnce()
    {
        var manager = Manager();
        Install(manager);
        manager.LoadAll();
        manager.Disable("com.example.test");

        manager.Enable("com.example.test");

        Assert.Equal(2, _loader.Loaded.Count);
        Assert.Equal(AddOnState.Running, Assert.Single(manager.List()).State);
    }

    [Fact]
    public void Remove_UnloadsAndDeletesIt()
    {
        var manager = Manager();
        Install(manager);
        manager.LoadAll();

        Assert.True(manager.Remove("com.example.test"));

        Assert.True(_loader.Loaded[0].Unloaded);
        Assert.Empty(manager.List());
        Assert.False(Directory.Exists(Path.Combine(_files.AddOnsDirectory, "com.example.test")));
    }

    [Fact]
    public void AnAddOnThatKeepsThrowing_IsTurnedOff_AndTheUserIsTold()
    {
        _loader.Set("com.example.test", () => new FakeAddOn(host =>
            host.AddCommand(Command("broken", () => throw new InvalidOperationException("bug")))));
        var manager = Manager();
        Install(manager);
        manager.LoadAll();
        var command = _keys!.Bindings[0].Command;

        for (int i = 0; i < AddOnGuard.MaxFailures; i++)
            manager.RunCommand(command);

        var info = Assert.Single(manager.List());
        Assert.Equal(AddOnState.TurnedOff, info.State);
        Assert.True(_loader.Loaded[0].Unloaded);
        Assert.Null(_keys);
        Assert.Contains(_said, s => s.Text.Contains("kept failing") && s.Text.Contains("Add-on com.example.test"));
        // Remembered: not loaded next time until the user turns it on
        var next = Manager();
        next.LoadAll();
        Assert.Single(_loader.Loaded);
        Assert.Equal(AddOnState.TurnedOff, Assert.Single(next.List()).State);
    }

    [Fact]
    public void AFewFailures_AreOnlyLogged()
    {
        int calls = 0;
        _loader.Set("com.example.test", () => new FakeAddOn(host => host.AddCommand(Command("flaky", () =>
        {
            if (++calls % 2 == 1)
                throw new InvalidOperationException("sometimes");
        }))));
        var manager = Manager();
        Install(manager);
        manager.LoadAll();

        for (int i = 0; i < 4; i++)
            manager.RunCommand(_keys!.Bindings[0].Command);

        Assert.Equal(AddOnState.Running, Assert.Single(manager.List()).State);
        Assert.Empty(_said);
    }

    [Fact]
    public void FailuresSpreadOverTime_DontTurnItOff()
    {
        var time = new ManualTimeProvider();
        var guard = new AddOnGuard("x", NullLogger.Instance, _ => Assert.Fail("turned off"), time);

        for (int i = 0; i < AddOnGuard.MaxFailures * 2; i++)
        {
            guard.Run(() => throw new InvalidOperationException(), "testing");
            time.Advance(TimeSpan.FromSeconds(20));
        }

        Assert.False(guard.IsStopped);
    }

    [Fact]
    public void AnAddOnThatHangs_IsTurnedOffOnceTheCallReturns_AndTheUserIsTold()
    {
        var time = new ManualTimeProvider();
        var posted = new Queue<Action>();
        var integration = new AddOnIntegration
        {
            Speak = (text, priority) => _said.Add((text, priority)),
            SetKeyLayer = layer => _keys = layer,
            Post = posted.Enqueue,
        };
        // The command holds the pipeline thread long enough for the watchdog's timer to fire
        _loader.Set("com.example.test", () => new FakeAddOn(host =>
            host.AddCommand(Command("stuck", () => time.Advance(TimeSpan.FromSeconds(3))))));
        var manager = Manager(integration: integration, time: time);
        Install(manager);
        manager.LoadAll();

        manager.RunCommand(_keys!.Bindings[0].Command);
        while (posted.TryDequeue(out var work))
            work();

        var info = Assert.Single(manager.List());
        Assert.Equal(AddOnState.TurnedOff, info.State);
        Assert.True(_loader.Loaded[0].Unloaded);
        Assert.Contains(_said, s => s.Text.Contains("stopped responding"));
        Assert.Equal(0, time.ActiveTimers);
    }

    [Fact]
    public void AnAddOnThatFailsToStart_IsUnloaded_AndReportedAsFailed()
    {
        _loader.Set("com.example.test", () => new FakeAddOn(_ => throw new InvalidOperationException("no")));
        var manager = Manager();
        Install(manager);

        manager.LoadAll();

        var info = Assert.Single(manager.List());
        Assert.Equal(AddOnState.Failed, info.State);
        Assert.True(_loader.Loaded[0].Unloaded);
    }

    [Fact]
    public void AnAddOnThatCantBeLoaded_IsReportedWithTheReason()
    {
        _loader.Throw = new AddOnLoadException("Test.dll has no public class implementing Vox.Sdk.IAddOn.");
        var manager = Manager();
        Install(manager);

        manager.LoadAll();

        var info = Assert.Single(manager.List());
        Assert.Equal(AddOnState.Failed, info.State);
        Assert.Contains("IAddOn", info.Problem);
    }

    [Fact]
    public void SafeMode_LoadsNoAddOn_ButTheyCanStillBeManaged()
    {
        var manager = Manager(RunPolicy.FromArgs(["--safe"]));
        Install(manager);

        manager.LoadAll();

        Assert.Empty(_loader.Loaded);
        Assert.True(manager.IsSafeMode);
        Assert.False(manager.CanLoad);
        Assert.True(manager.CanManage);
        var info = Assert.Single(manager.List());
        Assert.Equal(AddOnState.NotLoaded, info.State);
        Assert.Contains("safe mode", info.Problem);
        Assert.True(manager.Remove("com.example.test"));
    }

    [Fact]
    public void TheSecureScreens_NeverSeeAddOns()
    {
        Install(Manager());
        var manager = Manager(RunPolicy.Secure);

        manager.LoadAll();

        Assert.Empty(_loader.Loaded);
        Assert.Null(manager.Directory);
        Assert.Empty(manager.List());
        Assert.NotNull(manager.Unavailable);
        Assert.False(manager.Enable("com.example.test"));
    }

    [Fact]
    public void ThePolicy_CanTurnAddOnsOff()
    {
        Install(Manager());
        var manager = Manager(RunPolicy.Normal with { AddOnsOffByPolicy = true });

        manager.LoadAll();

        Assert.Empty(_loader.Loaded);
        Assert.Contains("organization", manager.Unavailable);
        Assert.Throws<InvalidOperationException>(() => manager.InstallFiles(manager.CheckPackage(_files.Package())));
    }

    [Fact]
    public void AnIncompatibleAddOn_IsRefused_WithTheReason()
    {
        var manager = Manager();
        var check = manager.CheckPackage(_files.Package(_files.Source(minApi: "9.0")));

        Assert.NotNull(check.Incompatible);
        Assert.Contains("needs a newer Vox", check.Incompatible);
        var error = Assert.Throws<AddOnPackageException>(() => manager.InstallFiles(check));
        Assert.Equal(check.Incompatible, error.Message);
        Assert.Empty(manager.List());
    }

    [Fact]
    public void AnIncompatibleAddOnAlreadyInstalled_IsListedButNotLoaded()
    {
        var manager = Manager();
        Install(manager);
        // Its manifest says it is for API 9.0 now (as if made for a later Vox)
        var manifestPath = Path.Combine(_files.AddOnsDirectory, "com.example.test", AddOnManifest.FileName);
        File.WriteAllText(manifestPath, (AddOnManifest.Parse(File.ReadAllText(manifestPath)) with { MinApiVersion = "9.0" }).ToJson());

        manager.LoadAll();

        Assert.Empty(_loader.Loaded);
        Assert.Equal(AddOnState.Incompatible, Assert.Single(manager.List()).State);
    }

    [Fact]
    public void APackageFromTheCatalogue_MustHaveItsChecksum()
    {
        var manager = Manager();
        var package = _files.Package();
        var sha = AddOnPackage.Read(package, AddOnTrustStore.Empty).Sha256;

        Assert.Equal(AddOnTrust.Catalogue, manager.CheckPackage(package, sha).Package.Trust);
        Assert.Throws<AddOnPackageException>(() => manager.CheckPackage(package, new string('0', 64)));
    }

    [Fact]
    public void AnUpdate_ReplacesTheRunningVersion()
    {
        var manager = Manager();
        Install(manager, version: "1.0.0");
        manager.LoadAll();

        var check = manager.CheckPackage(_files.Package(_files.Source(version: "1.1.0")));
        Assert.True(check.IsUpdate);
        Assert.Equal("1.0.0", check.Installed);
        manager.InstallFiles(check);
        manager.Reload("com.example.test");

        Assert.Equal(2, _loader.Loaded.Count);
        Assert.True(_loader.Loaded[0].Unloaded);
        Assert.False(_loader.Loaded[1].Unloaded);
        Assert.Equal("1.1.0", Assert.Single(manager.List()).Manifest.Version);
    }

    [Fact]
    public void AnnouncementFilters_ChangeSpeech_AndAThrowingOneIsSkipped()
    {
        _loader.Set("com.example.test", () => new FakeAddOn(host =>
        {
            host.AddAnnouncementFilter(new LambdaFilter(t => t.Replace("e.g.", "for example")));
            host.AddAnnouncementFilter(new LambdaFilter(_ => throw new InvalidOperationException()));
        }));
        var manager = Manager();
        Install(manager);
        manager.LoadAll();
        var rule = new AddOnTextRule(() => manager.Filters);

        var spoken = rule.Apply("Fruit, e.g. apples", new Utterance("Fruit, e.g. apples", Core.Speech.SpeechPriority.Normal));

        Assert.Equal("Fruit, for example apples", spoken);
        Assert.Equal(2, manager.Filters.Count);
    }

    [Fact]
    public void GlobalPlugins_SeeEventsAndFocus_ButNeverKeys()
    {
        var plugin = new RecordingPlugin();
        _loader.Set("com.example.test", () => new FakeAddOn(host => host.AddGlobalPlugin(plugin)));
        var manager = Manager();
        Install(manager);
        manager.LoadAll();
        var focus = new FocusChangedEvent(DateTimeOffset.UtcNow, "Password", "Edit", IsPassword: true, Value: "secret");

        Assert.Equal("Password edit (plugin)", manager.FormatFocusAnnouncement(focus, "Password edit"));
        Assert.False(manager.ShouldProcess(new Core.Pipeline.NotificationEvent(DateTimeOffset.UtcNow, "x", "drop me")));
        Assert.True(manager.ShouldProcess(new RawKeyEvent(DateTimeOffset.UtcNow, new KeyEvent { VkCode = 65 })));
        manager.OnEvent(focus);
        manager.OnEvent(new RawKeyEvent(DateTimeOffset.UtcNow, new KeyEvent { VkCode = 65 }));
        manager.OnEvent(new TypingEchoEvent(DateTimeOffset.UtcNow, "a", false));

        var seen = Assert.Single(plugin.Events);
        var focusEvent = Assert.IsType<FocusEvent>(seen);
        Assert.Null(focusEvent.Focus.Value);
        Assert.True(focusEvent.Focus.IsPassword);
    }

    [Fact]
    public void AppModules_GoToVox_WithTheirCommandsKeyedOnlyWhileActive()
    {
        int counted = 0;
        var module = new FakeSdkModule("notepadplus", "notepad++");
        module.CommandList.Add(Command("count", () => counted++, "VoxKey+Alt+F12"));
        _loader.Set("com.example.test", () => new FakeAddOn(host => host.AddAppModule(module)));
        var manager = Manager();
        Install(manager);
        manager.LoadAll();

        var adapter = Assert.IsType<AddOnAppModule>(Assert.Single(_modules));
        Assert.Equal("notepadplus", adapter.Id);
        Assert.Null(_keys);

        _active = adapter;
        manager.RefreshKeys();
        Assert.True(_keys!.TryResolveOutsideDocument(KeyModifiers.Insert | KeyModifiers.Alt, 0x7B, out var command));
        manager.RunCommand(command);
        Assert.Equal(1, counted);

        adapter.OnActivated(new Core.AppModules.AppActivation(4, "notepad++", 0));
        Assert.Equal(["on notepad++"], module.Log);
    }

    [Fact]
    public void ASpeechEngine_IsOffered_StartedWhenChosen_AndTakenAwayWhenItsAddOnStops()
    {
        var sdkEngine = new FakeSdkEngine();
        var registry = new SpeechEngineRegistry([
            new SpeechEngineDescriptor("OneCore", "OneCore", () => new RecordingSpeechEngine()),
            new SpeechEngineDescriptor("SAPI", "SAPI", () => new RecordingSpeechEngine()),
        ], NullLogger<SpeechEngineRegistry>.Instance);
        _loader.Set("com.example.test", () => new FakeAddOn(host => host.AddSpeechEngine(new SpeechEngineRegistration("Robot", "Robot voice", () => sdkEngine))));
        var manager = Manager(integration: Integration(registry, selectedEngine: "Robot"));
        Install(manager);

        manager.LoadAll();

        Assert.Equal(["OneCore", "Robot", "SAPI"], registry.Engines.Select(e => e.Id).ToList());
        Assert.Equal("Robot", registry.CurrentId);
        manager.Disable("com.example.test");
        Assert.Equal(["OneCore", "SAPI"], registry.Engines.Select(e => e.Id).ToList());
        Assert.Equal("OneCore", registry.CurrentId);
        Assert.True(sdkEngine.Disposed);
    }

    [Fact]
    public async Task AnAddOnsSpeechEngine_Speaks()
    {
        var sdkEngine = new FakeSdkEngine();
        var engine = new AddOnSpeechEngine(sdkEngine, new AddOnGuard("x", NullLogger.Instance, _ => { }));

        await engine.SpeakAsync(new Utterance("Hello", Core.Speech.SpeechPriority.Normal));

        Assert.Equal(["Hello"], sdkEngine.Spoken);
        Assert.Equal("en-GB", Assert.Single(engine.GetVoiceDetails()).Language);
    }

    [Fact]
    public void ABrailleDriver_IsTried_AndTakenAwayWhenItsAddOnStops()
    {
        var driver = new FakeSdkBrailleDriver();
        var settings = new TestOptionsMonitor<VoxSettings>(new VoxSettings());
        using var braille = new BrailleDisplayManager([], settings, NullLogger<BrailleDisplayManager>.Instance);
        _loader.Set("com.example.test", () => new FakeAddOn(host => host.AddBrailleDriver(driver)));
        var manager = Manager(integration: Integration(braille: braille));
        Install(manager);
        manager.LoadAll();
        var keys = new List<BrailleKeyEventArgs>();
        braille.KeyPressed += (_, e) => keys.Add(e);

        braille.Scan();
        driver.Press("dot1", "space");

        Assert.Equal("FakeDots", Assert.Single(braille.Drivers).Id);
        Assert.Equal(20, braille.Current.CellCount);
        Assert.Equal(["dot1", "space"], Assert.Single(keys).Keys);
        manager.Disable("com.example.test");
        Assert.Empty(braille.Drivers);
        Assert.False(braille.Current.IsConnected);
        Assert.True(driver.Disposed);
    }

    [Fact]
    public void AddOnsAddOnlyWhileTheyStart()
    {
        var manager = Manager();
        Install(manager);
        manager.LoadAll();
        var host = ((FakeAddOn)_loader.Loaded[0].Instance).Host!;

        Assert.Throws<InvalidOperationException>(() => host.AddCommand(Command("late", () => { })));
        Assert.EndsWith(Path.Combine(".data", "com.example.test"), host.DataDirectory);
        Assert.True(Directory.Exists(host.DataDirectory));
    }

    [Fact]
    public void AddOnsSpeakThroughVox()
    {
        _loader.Set("com.example.test", () => new FakeAddOn(host => host.Speech.Speak("Ready", Sdk.SpeechPriority.High)));
        var manager = Manager();
        Install(manager);

        manager.LoadAll();

        Assert.Equal(("Ready", Sdk.SpeechPriority.High), Assert.Single(_said));
    }

    [Fact]
    public void UnloadAll_LetsEveryAddOnGo()
    {
        var manager = Manager();
        Install(manager, "com.example.one");
        Install(manager, "com.example.two");
        manager.LoadAll();

        manager.UnloadAll();

        Assert.Equal(2, _loader.Loaded.Count);
        Assert.All(_loader.Loaded, l => Assert.True(l.Unloaded));
    }

    private sealed class LambdaFilter(Func<string, string?> filter) : IAnnouncementFilter
    {
        public string? Filter(string text, AnnouncementInfo info) => filter(text);
    }

    private sealed class RecordingPlugin : GlobalPlugin
    {
        public List<VoxEvent> Events { get; } = [];
        public override string? FormatFocusAnnouncement(FocusInfo focus, string announcement) => announcement + " (plugin)";
        public override bool ShouldProcess(VoxEvent evt) => evt is not Sdk.NotificationEvent { Text: "drop me" };
        public override void OnEvent(VoxEvent evt) => Events.Add(evt);
    }
}
