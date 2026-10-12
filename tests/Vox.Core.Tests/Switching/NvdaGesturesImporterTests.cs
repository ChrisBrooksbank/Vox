using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Switching;
using Xunit;

namespace Vox.Core.Tests.Switching;

public sealed class NvdaGesturesImporterTests
{
    private static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "Switching", "Samples", "nvda-folder", name);

    private static IReadOnlyList<ImportedKey> ParseSample(KeyboardLayout layout, out ImportReport report)
    {
        report = new ImportReport(ImportSource.Nvda);
        return NvdaGesturesImporter.Parse(File.ReadAllText(Sample("gestures.ini")), layout, report);
    }

    [Fact]
    public void Sample_MapsScriptsVoxHas_ToCommandsAndKeys()
    {
        var keys = ParseSample(KeyboardLayout.Desktop, out _);

        Assert.Contains(keys, k => k.Command == NavigationCommand.SayAll && k.Modifiers == KeyModifiers.Insert && k.VkCode == 'A' && k.DefaultMode == "Any");
        Assert.Contains(keys, k => k.Command == NavigationCommand.SayTime && k.Modifiers == (KeyModifiers.Insert | KeyModifiers.Ctrl) && k.VkCode == 'T');
        Assert.Contains(keys, k => k.Command == NavigationCommand.NextHeading && k.Modifiers == KeyModifiers.None && k.VkCode == 'J' && k.DefaultMode == "Browse");
        // A quoted comma key and a keypad key in one list
        Assert.Contains(keys, k => k.Command == NavigationCommand.NextLink && k.Modifiers == KeyModifiers.Insert && k.VkCode == 188);
        Assert.Contains(keys, k => k.Command == NavigationCommand.NextLink && k.VkCode == NumpadKeys.NumpadEnter);
        Assert.Equal(5, keys.Count);
    }

    [Fact]
    public void Sample_ReportsWhatVoxCantBind()
    {
        ParseSample(KeyboardLayout.Desktop, out var report);

        Assert.Contains(report.Skipped, s => s.Value == "kb(laptop):NVDA+shift+a" && s.Reason == ImportSkipReason.Unsupported);
        Assert.Contains(report.Skipped, s => s.Value == "kb:NVDA+windows+t" && s.Reason == ImportSkipReason.Unsupported);
        Assert.Contains(report.Skipped, s => s.Source.EndsWith("/speechMode", StringComparison.Ordinal) && s.Reason == ImportSkipReason.NoCounterpart);
        Assert.Contains(report.Skipped, s => s.Source.EndsWith("/None", StringComparison.Ordinal) && s.Reason == ImportSkipReason.Unsupported);
        Assert.Contains(report.Skipped, s => s.Value!.StartsWith("br(", StringComparison.Ordinal) && s.Reason == ImportSkipReason.NoCounterpart);
        Assert.Contains(report.Skipped, s => s.Source.StartsWith("appModules.", StringComparison.Ordinal) && s.Reason == ImportSkipReason.NoCounterpart);
        Assert.Contains(report.Skipped, s => s.Source == "gestures.ini:13" && s.Reason == ImportSkipReason.Malformed);
    }

    [Fact]
    public void LaptopLayout_TakesLaptopKeys_AndSkipsDesktopOnes()
    {
        var keys = ParseSample(KeyboardLayout.Laptop, out _);

        Assert.Contains(keys, k => k.Command == NavigationCommand.SayAll && k.Modifiers == (KeyModifiers.Insert | KeyModifiers.Shift) && k.VkCode == 'A');
    }

    [Theory]
    [InlineData("kb:NVDA+control+f", KeyModifiers.Insert | KeyModifiers.Ctrl, 'F')]
    [InlineData("kb:alt+shift+downArrow", KeyModifiers.Alt | KeyModifiers.Shift, 40)]
    [InlineData("kb:NVDA+f12", KeyModifiers.Insert, 123)]
    [InlineData("kb:numpad5", KeyModifiers.None, 101)]
    [InlineData("kb:NVDA+numpadMinus", KeyModifiers.Insert, 109)]
    [InlineData("kb:NVDA+[", KeyModifiers.Insert, 219)]
    [InlineData("KB:nvda+PAGEDOWN", KeyModifiers.Insert, 34)]
    public void Gesture_ParsesModifiersAndKey(string gesture, KeyModifiers modifiers, int vkCode)
    {
        Assert.True(NvdaGesturesImporter.TryParseGesture(gesture, KeyboardLayout.Desktop, out var m, out int vk, out _));
        Assert.Equal(modifiers, m);
        Assert.Equal(vkCode, vk);
    }

    [Theory]
    [InlineData("kb:NVDA+numLockNumpad5", ImportSkipReason.Unsupported)]
    [InlineData("ts:2finger_flickDown", ImportSkipReason.NoCounterpart)]
    [InlineData("kb:NVDA+volumeUp", ImportSkipReason.InvalidValue)]
    [InlineData("kb:hyper+a", ImportSkipReason.InvalidValue)]
    [InlineData("nonsense", ImportSkipReason.InvalidValue)]
    public void Gesture_VoxCantBind_SaysWhy(string gesture, ImportSkipReason reason)
    {
        Assert.False(NvdaGesturesImporter.TryParseGesture(gesture, KeyboardLayout.Desktop, out _, out _, out var why));
        Assert.Equal(reason, why);
    }

    [Fact]
    public void SplitGestures_KeepsCommaKeys()
    {
        Assert.Equal(["kb:NVDA+,", "kb:a", "kb:,"], NvdaGesturesImporter.SplitGestures("\"kb:NVDA+,\", kb:a, kb:,"));
    }
}
