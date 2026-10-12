using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Vox.Core.Trial;

/// <summary>The screen readers trial mode steps in for, and the watchdog falls back to.</summary>
public enum OtherScreenReader
{
    /// <summary>NVDA (nvda.exe).</summary>
    Nvda,
    /// <summary>Windows' own Narrator (Narrator.exe).</summary>
    Narrator,
}

/// <summary>A screen reader found running, with the program it runs from when known (to start it again).</summary>
public sealed record RunningScreenReader(OtherScreenReader Kind, string? ExecutablePath = null);

/// <summary>
/// Finds, stops and starts NVDA and Narrator: <see cref="WindowsScreenReaderControl"/> in Vox, a
/// fake in tests. Every member is safe to call from any thread and never throws for a screen
/// reader that isn't there.
/// </summary>
public interface IScreenReaderControl
{
    /// <summary>The other screen reader running now (NVDA first, then Narrator), or null.</summary>
    RunningScreenReader? FindRunning();

    /// <summary>Stops <paramref name="reader"/>; false when it is still running after trying.</summary>
    bool Stop(RunningScreenReader reader);

    /// <summary>Starts <paramref name="kind"/> (from <paramref name="executablePath"/> when given); false when it couldn't be started.</summary>
    bool Start(OtherScreenReader kind, string? executablePath = null);
}

/// <summary>
/// NVDA and Narrator on Windows. NVDA is found by its process and stopped the way it is asked to
/// quit from the command line (<c>nvda.exe -q</c>), or by ending its process when that fails;
/// it is started from where it ran from, else from where it is installed. Narrator is stopped
/// with its own shortcut (Windows+Ctrl+Enter), or by ending its process, and started from the
/// system folder.
/// </summary>
public sealed class WindowsScreenReaderControl(ILogger? logger = null) : IScreenReaderControl
{
    /// <summary>How long a screen reader is given to close.</summary>
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(4);

    public RunningScreenReader? FindRunning()
    {
        if (Find("nvda") is { } nvda)
            return new RunningScreenReader(OtherScreenReader.Nvda, nvda.Length > 0 ? nvda : InstalledNvda());
        if (Find("Narrator") is not null)
            return new RunningScreenReader(OtherScreenReader.Narrator, NarratorPath);
        return null;
    }

    public bool Stop(RunningScreenReader reader)
    {
        var name = ProcessName(reader.Kind);
        try
        {
            if (reader.Kind == OtherScreenReader.Nvda)
            {
                var path = string.IsNullOrEmpty(reader.ExecutablePath) ? InstalledNvda() : reader.ExecutablePath;
                if (path is not null && File.Exists(path))
                    Process.Start(new ProcessStartInfo(path, "-q") { UseShellExecute = false })?.Dispose();
            }
            else
            {
                PressNarratorShortcut();
            }
            if (WaitUntilGone(name, StopTimeout))
                return true;
            // It didn't close when asked: end it
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try { process.Kill(); }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
            }
            return WaitUntilGone(name, StopTimeout);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not stop {ScreenReader}", reader.Kind);
            return false;
        }
    }

    public bool Start(OtherScreenReader kind, string? executablePath = null)
    {
        try
        {
            var path = kind != OtherScreenReader.Nvda ? NarratorPath
                : string.IsNullOrEmpty(executablePath) ? InstalledNvda() : executablePath;
            if (path is null || !File.Exists(path))
            {
                logger?.LogWarning("{ScreenReader} is not installed", kind);
                return false;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not start {ScreenReader}", kind);
            return false;
        }
    }

    /// <summary>Where Narrator is.</summary>
    public static string NarratorPath => Path.Combine(Environment.SystemDirectory, "Narrator.exe");

    private static string ProcessName(OtherScreenReader kind) => kind == OtherScreenReader.Nvda ? "nvda" : "Narrator";

    // The program a running process was started from (empty string when it can't be read)
    private static string? Find(string name)
    {
        var processes = Process.GetProcessesByName(name);
        try
        {
            if (processes.Length == 0)
                return null;
            foreach (var process in processes)
            {
                try
                {
                    if (process.MainModule?.FileName is { Length: > 0 } path)
                        return path;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Another user's, or elevated: running, but its path can't be read
                }
            }
            return string.Empty;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private static bool WaitUntilGone(string name, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var process in processes)
                process.Dispose();
            if (processes.Length == 0)
                return true;
            if (DateTime.UtcNow > deadline)
                return false;
            Thread.Sleep(100);
        }
    }

    /// <summary>NVDA's nvda.exe as installed (its App Paths entry, else Program Files), or null.</summary>
    private static string? InstalledNvda()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\nvda.exe");
                if (key?.GetValue(null) is string registered && File.Exists(registered.Trim('"')))
                    return registered.Trim('"');
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
            }
        }
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            var path = Path.Combine(Environment.GetFolderPath(folder), "NVDA", "nvda.exe");
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    // Windows+Ctrl+Enter turns Narrator off (and on)
    private static void PressNarratorShortcut()
    {
        if (!OperatingSystem.IsWindows())
            return;
        const byte LWin = 0x5B, Control = 0x11, Return = 0x0D;
        const uint KeyUp = 0x0002;
        keybd_event(LWin, 0, 0, UIntPtr.Zero);
        keybd_event(Control, 0, 0, UIntPtr.Zero);
        keybd_event(Return, 0, 0, UIntPtr.Zero);
        keybd_event(Return, 0, KeyUp, UIntPtr.Zero);
        keybd_event(Control, 0, KeyUp, UIntPtr.Zero);
        keybd_event(LWin, 0, KeyUp, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
