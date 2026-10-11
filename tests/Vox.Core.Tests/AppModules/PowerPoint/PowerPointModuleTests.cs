using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.AppModules;
using Vox.Core.AppModules.PowerPoint;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.AppModules.PowerPoint;

public class PowerPointFormatterTests
{
    private static readonly PowerPointShape Title = new("Title 1", 14, "Results\r", PlaceholderType: 1);
    private static readonly PowerPointShape Body = new("Content 2", 14, "Up 5%\rDown 2%", PlaceholderType: 2);
    private static readonly PowerPointShape Picture = new("Picture 3", 13, AltText: "Sales chart");

    private static PowerPointSlide Slide(bool show = false) => new(3, 10, "Results", [Title, Body, Picture], "Say hello", show);

    [Fact]
    public void SlideChanged() => Assert.Equal("Slide 3 of 10, Results", PowerPointFormatter.SlideChanged(Slide()));

    [Fact]
    public void SlideShowSlide_ReadsEverythingButTheTitleAgain() =>
        Assert.Equal("Slide 3 of 10, Results. Up 5% Down 2%. picture, Sales chart", PowerPointFormatter.SlideShowSlide(Slide()));

    [Fact]
    public void Shape()
    {
        Assert.Equal("Title 1, title, Results, 1 of 3", PowerPointFormatter.Shape(Title, 1, 3));
        Assert.Equal("Picture 3, picture, Sales chart, 3 of 3", PowerPointFormatter.Shape(Picture, 3, 3));
    }

    [Fact]
    public void Notes()
    {
        Assert.Equal("Notes: Say hello", PowerPointFormatter.Notes(Slide()));
        Assert.Equal("Slide 3 has no notes", PowerPointFormatter.Notes(Slide() with { Notes = " " }));
    }
}

public class PowerPointModuleTests : IDisposable
{
    private sealed class FakePowerPoint : IPowerPointSource
    {
        public PowerPointSlide? Slide { get; set; }
        public List<int> Selected { get; } = [];

        public Task<PowerPointSlide?> CurrentSlideAsync(IntPtr window) => Task.FromResult(Slide);

        public Task<string?> SelectShapeAsync(IntPtr window, int index)
        {
            Selected.Add(index);
            return Task.FromResult<string?>("selected");
        }
    }

    private sealed class InlineSink : IEventSink
    {
        public void Post(ScreenReaderEvent evt)
        {
            if (evt is PipelineWorkEvent work)
                lock (this) work.Work();
        }
    }

    private readonly FakePowerPoint _powerPoint = new();
    private readonly RecordingSpeechEngine _engine = new();
    private readonly SpeechQueue _speech;
    private readonly PowerPointModule _module;

    public PowerPointModuleTests()
    {
        _speech = new SpeechQueue(_engine, NullLogger<SpeechQueue>.Instance);
        _module = new PowerPointModule(_powerPoint, _speech, new InlineSink(), slideCheckDelay: TimeSpan.FromMilliseconds(1));
        _module.OnActivated(new AppActivation(1, "POWERPNT", new IntPtr(9)));
    }

    public void Dispose() => _speech.Dispose();

    private static PowerPointSlide Slide(int number, bool show = false) =>
        new(number, 5, $"Slide title {number}", [new PowerPointShape("Title 1", 14, $"Slide title {number}", PlaceholderType: 1), new PowerPointShape("Text 2", 17, "Body")], InSlideShow: show);

    private static RawKeyEvent Key(int vk) => new(DateTimeOffset.UtcNow, new KeyEvent { VkCode = vk, IsKeyDown = true });

    [Fact]
    public async Task ChangingSlide_SaysItsNumberAndTitle_ButNotTheFirstSlideSeen()
    {
        _powerPoint.Slide = Slide(1);
        _module.OnEvent(new FocusChangedEvent(DateTimeOffset.UtcNow, "Slide 1", "ListItem"));
        await Task.Delay(100);
        Assert.Empty(_engine.SpokenText);

        _powerPoint.Slide = Slide(2);
        _module.OnEvent(Key(0x22));

        Assert.Equal(SpeechPriority.High, (await _engine.WaitForTextAsync("Slide 2 of 5, Slide title 2")).Priority);
    }

    [Fact]
    public async Task SlideShow_ReadsTheSlidesText()
    {
        _powerPoint.Slide = Slide(1, show: true);

        _module.OnEvent(Key(0x20));

        await _engine.WaitForTextAsync("Slide 1 of 5, Slide title 1. Body");
    }

    [Fact]
    public async Task OtherKeys_DontCheckTheSlide()
    {
        _powerPoint.Slide = Slide(1, show: true);

        _module.OnEvent(Key(0x41));
        await Task.Delay(100);

        Assert.Empty(_engine.SpokenText);
        Assert.True(PowerPointModule.MayChangeSlide(0x22));
        Assert.False(PowerPointModule.MayChangeSlide(0x41));
    }

    [Fact]
    public async Task ShapeNavigation_SelectsAndReads()
    {
        _powerPoint.Slide = Slide(1);

        _module.TryHandle(NavigationCommand.PowerPointNextShape);
        await _engine.WaitForTextAsync("Title 1, title, Slide title 1, 1 of 2");
        _module.TryHandle(NavigationCommand.PowerPointNextShape);
        await _engine.WaitForTextAsync("Text 2, text box, Body, 2 of 2");
        _module.TryHandle(NavigationCommand.PowerPointNextShape);
        await _engine.WaitForTextAsync("No next shape");

        Assert.Equal([1, 2], _powerPoint.Selected);
    }

    [Fact]
    public async Task Notes_Command()
    {
        _powerPoint.Slide = Slide(1) with { Notes = "Thank everyone" };

        Assert.True(_module.TryHandle(NavigationCommand.PowerPointNotes));

        await _engine.WaitForTextAsync("Notes: Thank everyone");
    }

    [Fact]
    public void KeyMap_BindsEveryPowerPointCommand()
    {
        var bound = AppModuleKeyMaps.Load("powerpoint").Select(b => b.Command).ToHashSet();
        foreach (var command in Enum.GetValues<NavigationCommand>().Where(c => c.ToString().StartsWith("PowerPoint", StringComparison.Ordinal)))
            Assert.Contains(command.ToString(), bound);
    }
}
