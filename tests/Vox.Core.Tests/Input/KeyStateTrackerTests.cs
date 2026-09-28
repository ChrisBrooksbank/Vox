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

    [Fact]
    public void Reconcile_ClearsModifiersNotPhysicallyDown()
    {
        var tracker = new KeyStateTracker();
        tracker.Process(KeyStateTracker.VK_LCONTROL, true, out _);
        tracker.Process(KeyStateTracker.VK_LMENU, true, out _);

        // Ctrl+Alt+Del: the key-ups happened on the secure desktop; only Alt is still held
        tracker.Reconcile(vk => vk == KeyStateTracker.VK_LMENU);

        Assert.Equal(KeyModifiers.Alt, tracker.Current);
    }

    [Fact]
    public void Reset_ClearsEverythingIncludingScreenReaderModifier()
    {
        var tracker = new KeyStateTracker();
        tracker.Process(KeyStateTracker.VK_INSERT, true, out _);
        tracker.Process(KeyStateTracker.VK_RSHIFT, true, out _);

        tracker.Reset();

        Assert.Equal(KeyModifiers.None, tracker.Current);
    }
}

public class RepeatPressConfirmationTests
{
    [Fact]
    public void SecondPressWithinWindow_Confirms()
    {
        long now = 1000;
        var confirmation = new RepeatPressConfirmation(TimeSpan.FromSeconds(3), () => now);

        Assert.False(confirmation.Press());
        now += 2000;
        Assert.True(confirmation.Press());
        // Confirmed: the next press starts over
        now += 100;
        Assert.False(confirmation.Press());
    }

    [Fact]
    public void SecondPressAfterWindow_StartsOver()
    {
        long now = 1000;
        var confirmation = new RepeatPressConfirmation(TimeSpan.FromSeconds(3), () => now);

        Assert.False(confirmation.Press());
        now += 5000;
        Assert.False(confirmation.Press());
        now += 1000;
        Assert.True(confirmation.Press());
    }
}

public class ModifierTapDetectorTests
{
    [Fact]
    public void SecondQuickTap_PassesThrough()
    {
        var taps = new ModifierTapDetector();
        Assert.False(taps.OnModifierDown(1000, isRepeat: false));
        taps.OnModifierUp(1080);
        Assert.True(taps.OnModifierDown(1300, isRepeat: false));
    }

    [Fact]
    public void SlowSecondTap_IsSwallowed()
    {
        var taps = new ModifierTapDetector();
        taps.OnModifierDown(1000, isRepeat: false);
        taps.OnModifierUp(1080);
        Assert.False(taps.OnModifierDown(1080 + ModifierTapDetector.DoubleTapMs + 1, isRepeat: false));
    }

    [Fact]
    public void ModifierUsedForACommand_IsNotATap()
    {
        var taps = new ModifierTapDetector();
        taps.OnModifierDown(1000, isRepeat: false);
        taps.OnOtherKeyDown(); // Insert+Down
        taps.OnModifierUp(1100);
        Assert.False(taps.OnModifierDown(1200, isRepeat: false));
    }

    [Fact]
    public void AutoRepeat_DoesNotCountAsASecondTap()
    {
        var taps = new ModifierTapDetector();
        taps.OnModifierDown(1000, isRepeat: false);
        Assert.False(taps.OnModifierDown(1030, isRepeat: true));
        taps.OnModifierUp(1100);
        Assert.True(taps.OnModifierDown(1200, isRepeat: false));
    }

    [Fact]
    public void ThirdTap_StartsOver()
    {
        var taps = new ModifierTapDetector();
        taps.OnModifierDown(1000, false); taps.OnModifierUp(1050);
        Assert.True(taps.OnModifierDown(1100, false)); taps.OnModifierUp(1150);
        Assert.False(taps.OnModifierDown(1200, false));
    }
}
