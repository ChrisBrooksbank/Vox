using Vox.Core.Lifecycle;
using Xunit;

namespace Vox.Core.Tests.Lifecycle;

public sealed class PortableCopyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VoxPortable_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Make(string relative, string content = "x")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void CopiesTheProgram_AndMakesTheCopyPortable()
    {
        Make("program/Vox.App.exe");
        Make("program/assets/config/default-keymap.json");
        var target = Path.Combine(_root, "usb", "Vox");

        PortableCopy.Create(Path.Combine(_root, "program"), target);

        Assert.True(File.Exists(Path.Combine(target, "Vox.App.exe")));
        Assert.True(File.Exists(Path.Combine(target, "assets", "config", "default-keymap.json")));
        Assert.True(VoxPaths.IsPortableAt(target));
        Assert.False(VoxPaths.IsPortableAt(Path.Combine(_root, "program")));
    }

    [Fact]
    public void TakesTheUsersSettingsAndComponents_ButNotLogs()
    {
        Make("program/Vox.App.exe");
        Make("appdata/settings.json", "{}");
        Make("appdata/keymap.json", "{}");
        Make("appdata/dictionaries/user.json", "[]");
        Make("appdata/logs/vox-1.log");
        Make("local/components/espeak-ng/libespeak-ng.dll");
        var target = Path.Combine(_root, "usb");

        PortableCopy.Create(Path.Combine(_root, "program"), target, Path.Combine(_root, "appdata"), Path.Combine(_root, "local", "components"));

        var data = Path.Combine(target, VoxPaths.PortableFolderName);
        Assert.True(File.Exists(Path.Combine(data, "settings.json")));
        Assert.True(File.Exists(Path.Combine(data, "keymap.json")));
        Assert.True(File.Exists(Path.Combine(data, "dictionaries", "user.json")));
        Assert.True(File.Exists(Path.Combine(data, "components", "espeak-ng", "libespeak-ng.dll")));
        Assert.False(Directory.Exists(Path.Combine(data, "logs")));
    }

    [Fact]
    public void CopyingAPortableCopy_LeavesItsOwnSettingsBehind()
    {
        Make("usb1/Vox.App.exe");
        Make($"usb1/{VoxPaths.PortableFolderName}/settings.json", "first stick");
        var target = Path.Combine(_root, "usb2");

        PortableCopy.Create(Path.Combine(_root, "usb1"), target);

        Assert.False(File.Exists(Path.Combine(target, VoxPaths.PortableFolderName, "settings.json")));
        Assert.True(VoxPaths.IsPortableAt(target));
    }

    [Fact]
    public void ACopyInsideVoxsOwnFolder_IsRefused()
    {
        Make("program/Vox.App.exe");

        Assert.Throws<ArgumentException>(() =>
            PortableCopy.Create(Path.Combine(_root, "program"), Path.Combine(_root, "program", "copy")));
    }
}
