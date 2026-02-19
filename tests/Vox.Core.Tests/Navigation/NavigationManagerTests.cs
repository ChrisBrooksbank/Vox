using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Audio;
using Vox.Core.Buffer;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class NavigationManagerTests
{
    private readonly Mock<IAudioCuePlayer> _audioCue = new();
    private readonly Mock<IEventSink> _pipeline = new();

    private NavigationManager CreateManager() => new(
        _audioCue.Object,
        _pipeline.Object,
        NullLogger<NavigationManager>.Instance);

    // -------------------------------------------------------------------------
    // Initial state
    // -------------------------------------------------------------------------

    [Fact]
    public void InitialMode_IsBrowse()
    {
        var nm = CreateManager();
        Assert.Equal(InteractionMode.Browse, nm.CurrentMode);
    }

    // -------------------------------------------------------------------------
    // ToggleMode
    // -------------------------------------------------------------------------

    [Fact]
    public void ToggleMode_FromBrowse_SwitchesToFocus()
    {
        var nm = CreateManager();
        nm.ToggleMode();
        Assert.Equal(InteractionMode.Focus, nm.CurrentMode);
    }

    [Fact]
    public void ToggleMode_FromFocus_SwitchesToBrowse()
    {
        var nm = CreateManager();
        nm.ToggleMode(); // -> Focus
        nm.ToggleMode(); // -> Browse
        Assert.Equal(InteractionMode.Browse, nm.CurrentMode);
    }

    [Fact]
    public void ToggleMode_PlaysAudioCue_FocusMode()
    {
        var nm = CreateManager();
        nm.ToggleMode();
        _audioCue.Verify(a => a.Play("focus_mode"), Times.Once);
    }

    [Fact]
    public void ToggleMode_PlaysAudioCue_BrowseMode()
    {
        var nm = CreateManager();
        nm.ToggleMode(); // -> Focus
        nm.ToggleMode(); // -> Browse
        _audioCue.Verify(a => a.Play("browse_mode"), Times.Once);
    }

    [Fact]
    public void ToggleMode_PostsModeChangedEvent()
    {
        var nm = CreateManager();
        nm.ToggleMode();
        _pipeline.Verify(p => p.Post(It.Is<ModeChangedEvent>(e => e.NewMode == InteractionMode.Focus)), Times.Once);
    }

    // -------------------------------------------------------------------------
    // HandleCommand — ToggleMode command
    // -------------------------------------------------------------------------

    [Fact]
    public void HandleCommand_ToggleMode_SwitchesMode()
    {
        var nm = CreateManager();
        bool handled = nm.HandleCommand(NavigationCommand.ToggleMode, null);
        Assert.True(handled);
        Assert.Equal(InteractionMode.Focus, nm.CurrentMode);
    }

    // -------------------------------------------------------------------------
    // HandleCommand — Focus mode blocks navigation commands
    // -------------------------------------------------------------------------

    [Fact]
    public void HandleCommand_InFocusMode_BlocksNavCommands()
    {
        var nm = CreateManager();
        nm.SwitchTo(InteractionMode.Focus);

        bool handled = nm.HandleCommand(NavigationCommand.NextHeading, null);
        Assert.True(handled); // blocked/swallowed
    }

    [Fact]
    public void HandleCommand_InBrowseMode_PassesNavCommands()
    {
        var nm = CreateManager();

        bool handled = nm.HandleCommand(NavigationCommand.NextHeading, null);
        Assert.False(handled); // not consumed — should be handled by QuickNavHandler
    }

    // -------------------------------------------------------------------------
    // Auto-switch: ActivateElement on edit field -> Focus mode
    // -------------------------------------------------------------------------

    [Fact]
    public void HandleCommand_ActivateEditField_SwitchesToFocusMode()
    {
        var nm = CreateManager();
        var editNode = new VBufferNode { ControlType = "Edit", IsFocusable = true };

        nm.HandleCommand(NavigationCommand.ActivateElement, editNode);

        Assert.Equal(InteractionMode.Focus, nm.CurrentMode);
        _audioCue.Verify(a => a.Play("focus_mode"), Times.Once);
    }

    [Fact]
    public void HandleCommand_ActivateNonFormElement_DoesNotSwitchMode()
    {
        var nm = CreateManager();
        var linkNode = new VBufferNode { ControlType = "Hyperlink", IsLink = true };

        nm.HandleCommand(NavigationCommand.ActivateElement, linkNode);

        Assert.Equal(InteractionMode.Browse, nm.CurrentMode);
    }

    [Fact]
    public void HandleCommand_ActivateElement_DoesNotConsumeEvent()
    {
        var nm = CreateManager();
        var editNode = new VBufferNode { ControlType = "Edit", IsFocusable = true };

        bool handled = nm.HandleCommand(NavigationCommand.ActivateElement, editNode);

        Assert.False(handled); // activation should still proceed
    }

    // -------------------------------------------------------------------------
    // Auto-switch: FocusChanged leaves form -> Browse mode
    // -------------------------------------------------------------------------

    [Fact]
    public void HandleFocusChanged_FromFormField_ToNonForm_SwitchesToBrowse()
    {
        var nm = CreateManager();
        nm.SwitchTo(InteractionMode.Focus);

        var focusEvt = new FocusChangedEvent(DateTimeOffset.UtcNow, "Heading", "Heading");
        nm.HandleFocusChanged(focusEvt);

        Assert.Equal(InteractionMode.Browse, nm.CurrentMode);
    }

    [Fact]
    public void HandleFocusChanged_InBrowseMode_NoChange()
    {
        var nm = CreateManager(); // already Browse
        var focusEvt = new FocusChangedEvent(DateTimeOffset.UtcNow, "Paragraph", "Text");
        nm.HandleFocusChanged(focusEvt);

        Assert.Equal(InteractionMode.Browse, nm.CurrentMode);
        // No extra SwitchTo calls
        _pipeline.Verify(p => p.Post(It.IsAny<ModeChangedEvent>()), Times.Never);
    }

    [Fact]
    public void HandleFocusChanged_ToEditField_StaysInFocusMode()
    {
        var nm = CreateManager();
        nm.SwitchTo(InteractionMode.Focus);

        var focusEvt = new FocusChangedEvent(DateTimeOffset.UtcNow, "Search", "Edit");
        nm.HandleFocusChanged(focusEvt);

        Assert.Equal(InteractionMode.Focus, nm.CurrentMode);
    }

    // -------------------------------------------------------------------------
    // SwitchTo — idempotent
    // -------------------------------------------------------------------------

    [Fact]
    public void SwitchTo_SameMode_IsNoOp()
    {
        var nm = CreateManager();
        nm.SwitchTo(InteractionMode.Browse); // already Browse

        _pipeline.Verify(p => p.Post(It.IsAny<ModeChangedEvent>()), Times.Never);
        _audioCue.Verify(a => a.Play(It.IsAny<string>()), Times.Never);
    }
}
