using Vox.Core.Configuration;
using Vox.Core.Input;
using Xunit;

namespace Vox.Core.Tests.Input;

public class KeyStateTrackerTests
{
    [Theory]
    [InlineData(KeyStateTracker.VK_LSHIFT, KeyModifiers.Shift)]
    [InlineData(KeyStateTracker.VK_RSHIFT, KeyModifiers.Shift)]
    [InlineData(KeyStateTracker.VK_LCONTROL, KeyModifiers.Ctrl)]
    [InlineData(KeyStateTracker.VK_RCONTROL, KeyModifiers.Ctrl)]
    [InlineData(KeyStateTracker.VK_LMENU, KeyModifiers.Alt)]
    [InlineData(KeyStateTracker.VK_RMENU, KeyModifiers.Alt)]
    public void LeftRightModifierKeys_AreTracked(int vk, KeyModifiers expected)
    {
        var tracker = new KeyStateTracker();

        tracker.Process(vk, isKeyDown: true, out _);
        Assert.Equal(expected, tracker.Process(0x48, isKeyDown: true, out _));

        tracker.Process(vk, isKeyDown: false, out _);
        Assert.Equal(KeyModifiers.None, tracker.Current);
    }

    [Fact]
    public void Process_ModifierKeyItself_ReportsModifiersExcludingIt()
    {
        var tracker = new KeyStateTracker();

        Assert.Equal(KeyModifiers.None, tracker.Process(KeyStateTracker.VK_LCONTROL, true, out _));
        Assert.Equal(KeyModifiers.None, tracker.Process(KeyStateTracker.VK_LCONTROL, false, out _));
    }

    [Fact]
    public void Insert_IsScreenReaderModifier_ByDefault()
    {
        var tracker = new KeyStateTracker();

        tracker.Process(KeyStateTracker.VK_INSERT, true, out var isModifier);
        Assert.True(isModifier);
        Assert.Equal(KeyModifiers.Insert, tracker.Process(0x20, true, out _));
    }

    [Fact]
    public void CapsLock_AsScreenReaderModifier_ReportsInsertFlag_AndDoesNotToggle()
    {
        var tracker = new KeyStateTracker { ScreenReaderModifier = ModifierKey.CapsLock };

        tracker.Process(KeyStateTracker.VK_CAPITAL, true, out var isModifier);
        Assert.True(isModifier);
        Assert.Equal(KeyModifiers.Insert, tracker.Process(0x20, true, out _));
        Assert.False(tracker.CapsLockOn);

        tracker.Process(KeyStateTracker.VK_INSERT, true, out var insertIsModifier);
        Assert.False(insertIsModifier);
    }

    [Fact]
    public void CapsLock_TogglesOnPress_IgnoringAutoRepeat()
    {
        var tracker = new KeyStateTracker();

        tracker.Process(KeyStateTracker.VK_CAPITAL, true, out _);
        tracker.Process(KeyStateTracker.VK_CAPITAL, true, out _); // auto-repeat
        tracker.Process(KeyStateTracker.VK_CAPITAL, false, out _);
        Assert.True(tracker.CapsLockOn);

        tracker.Process(KeyStateTracker.VK_CAPITAL, true, out _);
        tracker.Process(KeyStateTracker.VK_CAPITAL, false, out _);
        Assert.False(tracker.CapsLockOn);
    }
}
