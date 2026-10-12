using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Accessibility;
using Vox.Core.AddOns;
using Vox.Core.AppModules;
using Vox.Core.Braille;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Speech;
using Vox.Core.Tests.AddOns;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.AppModules;

public class AddOnAppModuleTests
{
    private sealed class FakeForeground : IForegroundWindow
    {
        public ForegroundWindowInfo? Get() => null;
    }

    private sealed class BuiltIn(string id, params string[] processes) : AppModule
    {
        public override string Id => id;
        public override string Name => id;
        public override IReadOnlyCollection<string> ProcessNames => processes;
    }

    [Fact]
    public void AnAddOnsModule_ComesBeforeVoxsOwnForTheSameApplication_AndCanBeTurnedOff()
    {
        var settings = new TestOptionsMonitor<VoxSettings>(new VoxSettings());
        var changes = new List<IAppModule?>();
        var manager = new AppModuleManager([new BuiltIn("word", "WINWORD")], new FakeForeground(), _ => { }, settings,
            processName: _ => "WINWORD");
        manager.ActiveModuleChanged += (_, m) => changes.Add(m);
        var addOn = new AddOnAppModule(new FakeSdkModule("betterword", "winword"), new AddOnGuard("x", NullLogger.Instance, _ => { }));

        manager.Update(7);
        Assert.Equal("word", manager.ActiveModule!.Id);
        manager.SetAddOnModules([addOn]);

        Assert.Same(addOn, manager.ActiveModule);
        Assert.Equal(["word", "betterword"], changes.Select(c => c!.Id).ToList());
        settings.Set(new VoxSettings { DisabledAppModules = ["betterword"] });
        manager.Reselect();
        Assert.Equal("word", manager.ActiveModule!.Id);
    }
}

public class SpeechEngineRegistryAddOnTests
{
    [Fact]
    public void AnAddedEngine_GoesBeforeTheFallback_AndRemovingTheOneInUseStartsThePreferred()
    {
        var registry = new SpeechEngineRegistry([
            new SpeechEngineDescriptor("OneCore", "OneCore", () => new RecordingSpeechEngine()),
            new SpeechEngineDescriptor("SAPI", "SAPI", () => new RecordingSpeechEngine()),
        ], NullLogger<SpeechEngineRegistry>.Instance);
        var changes = new List<string>();
        registry.EngineChanged += (_, id) => changes.Add(id);

        Assert.True(registry.Add(new SpeechEngineDescriptor("Robot", "Robot", () => new RecordingSpeechEngine())));
        Assert.False(registry.Add(new SpeechEngineDescriptor("robot", "Robot again", () => new RecordingSpeechEngine())));
        registry.Select("Robot");
        registry.Remove("Robot");

        Assert.Equal(["OneCore", "SAPI"], registry.Engines.Select(e => e.Id).ToList());
        Assert.Equal("OneCore", registry.CurrentId);
        Assert.Equal(["Robot", "OneCore"], changes);
    }
}

public class BrailleLineShownTests
{
    [Fact]
    public void EveryLineShown_IsReported()
    {
        var display = new FakeBrailleDisplay(40);
        var source = new FakeBrailleDisplaySource(display);
        var settings = new TestOptionsMonitor<VoxSettings>(new VoxSettings { BrailleMessageTimeoutSeconds = 0 });
        using var controller = new BrailleController(source, settings, new Moq.Mock<IBrailleRouter>().Object,
            new Moq.Mock<IKeyboardOutput>().Object, NullLogger<BrailleController>.Instance, _ => new FakeBrailleTranslator());
        var lines = new List<BrailleLine>();
        controller.LineShown += (_, line) => lines.Add(line);

        controller.ShowReviewLine("Hello world", 6);

        var line = Assert.Single(lines);
        Assert.Equal("Hello world", line.Text);
        Assert.Equal(6, line.CursorOffset);
    }
}
