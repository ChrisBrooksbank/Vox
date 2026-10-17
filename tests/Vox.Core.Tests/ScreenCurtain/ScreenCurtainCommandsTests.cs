using Microsoft.Extensions.Options;
using Moq;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Localization;
using Vox.Core.ScreenCurtain;
using Xunit;

namespace Vox.Core.Tests.ScreenCurtain;

public class ScreenCurtainCommandsTests
{
    private sealed class FakeCurtain : IScreenCurtain
    {
        public bool CanApply = true;
        public int Removed;
        public bool IsActive { get; private set; }
        public bool Apply() => IsActive = CanApply;
        public void Remove() { IsActive = false; Removed++; }
        public void Dispose() => Remove();
    }

    private readonly FakeCurtain _curtain = new();
    private readonly List<string> _said = [];
    private VoxSettings _settings = new();

    private ScreenCurtainCommands Create(RunPolicy? policy = null)
    {
        var monitor = new Mock<IOptionsMonitor<VoxSettings>>();
        monitor.Setup(m => m.CurrentValue).Returns(() => _settings);
        return new ScreenCurtainCommands(_curtain, monitor.Object, _said.Add, policy: policy);
    }

    [Fact]
    public void OtherCommands_AreNotHandled()
    {
        Assert.False(Create().TryHandle(NavigationCommand.NextHeading));
        Assert.False(_curtain.IsActive);
    }

    [Fact]
    public void Toggle_TurnsOnAndOff_SayingEach()
    {
        var commands = Create();

        Assert.True(commands.TryHandle(NavigationCommand.ToggleScreenCurtain));
        Assert.True(_curtain.IsActive);
        Assert.Equal(VoxStrings.Get("ScreenCurtain.On"), _said[^1]);

        commands.Toggle();
        Assert.False(_curtain.IsActive);
        Assert.Equal(VoxStrings.Get("ScreenCurtain.Off"), _said[^1]);
    }

    [Fact]
    public void Toggle_WhenTheCurtainCannotBeApplied_SaysSo()
    {
        _curtain.CanApply = false;

        Create().Toggle();

        Assert.False(_curtain.IsActive);
        Assert.Equal(VoxStrings.Get("ScreenCurtain.Failed"), Assert.Single(_said));
    }

    [Fact]
    public void Toggle_OnSecureScreens_IsRefused()
    {
        Create(new RunPolicy(true)).Toggle();

        Assert.False(_curtain.IsActive);
        Assert.Equal(VoxStrings.Get("ScreenCurtain.NotAvailable"), Assert.Single(_said));
    }

    [Fact]
    public void StartIfWanted_WithTheSetting_TurnsOnWithAWarning()
    {
        _settings = _settings with { ScreenCurtainAtStart = true };

        Create().StartIfWanted();

        Assert.True(_curtain.IsActive);
        Assert.Equal(VoxStrings.Get("ScreenCurtain.OnAtStart"), Assert.Single(_said));
    }

    [Fact]
    public void StartIfWanted_WithoutTheSetting_DoesNothing()
    {
        Create().StartIfWanted();

        Assert.False(_curtain.IsActive);
        Assert.Empty(_said);
    }

    [Fact]
    public void Dispose_TakesTheCurtainOff()
    {
        var commands = Create();
        commands.Toggle();

        commands.Dispose();

        Assert.False(_curtain.IsActive);
    }
}
