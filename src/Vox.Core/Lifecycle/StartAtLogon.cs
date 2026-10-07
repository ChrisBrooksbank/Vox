using Microsoft.Win32;

namespace Vox.Core.Lifecycle;

/// <summary>Where "start at logon" is recorded.</summary>
public interface IStartupRegistration
{
    /// <summary>The command registered to run at logon, or null.</summary>
    string? Command { get; }

    void Register(string command);
    void Unregister();
}

/// <summary>The current user's Run key (HKCU\Software\Microsoft\Windows\CurrentVersion\Run): no admin rights needed.</summary>
public sealed class RunKeyStartupRegistration : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Vox";

    public string? Command
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) as string;
        }
    }

    public void Register(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, command);
    }

    public void Unregister()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

/// <summary>
/// Applies the "start at logon" setting: registers Vox (through Vox.Watchdog when it is installed
/// alongside, so a crash restarts it) or removes the registration. Not in secure mode.
/// </summary>
public static class StartAtLogon
{
    /// <summary>The command to run at logon for Vox installed in <paramref name="installDirectory"/>.</summary>
    public static string CommandFor(string installDirectory, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        var watchdog = Path.Combine(installDirectory, "Vox.Watchdog.exe");
        var app = Path.Combine(installDirectory, "Vox.App.exe");
        return $"\"{(fileExists(watchdog) ? watchdog : app)}\"";
    }

    /// <summary>Makes the registration match <paramref name="enabled"/>. Returns whether anything changed.</summary>
    public static bool Apply(bool enabled, IStartupRegistration registration, string command, RunPolicy policy)
    {
        if (policy.IsSecure)
            return false;
        var current = registration.Command;
        if (enabled && current != command)
        {
            registration.Register(command);
            return true;
        }
        if (!enabled && current is not null)
        {
            registration.Unregister();
            return true;
        }
        return false;
    }
}
