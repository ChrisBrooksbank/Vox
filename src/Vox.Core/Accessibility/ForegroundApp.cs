using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Vox.Core.Accessibility;

/// <summary>The application that owns the foreground window.</summary>
public readonly record struct ForegroundAppInfo(int ProcessId, string Name);

/// <summary>
/// Finds the foreground application without UIA, so it still works while UIA calls are hanging.
/// </summary>
public interface IForegroundApp
{
    /// <summary>The foreground application, or null when there is no foreground window.</summary>
    ForegroundAppInfo? Get();
}

/// <summary>
/// <see cref="IForegroundApp"/> through GetForegroundWindow / GetWindowThreadProcessId. The name is
/// the executable's file description ("Google Chrome") when readable, else the process name.
/// </summary>
public sealed class Win32ForegroundApp : IForegroundApp
{
    public ForegroundAppInfo? Get()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
            return null;
        return new ForegroundAppInfo((int)pid, GetAppName((int)pid));
    }

    /// <summary>A name for the process to speak: its file description, or its process name.</summary>
    public static string GetAppName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            try
            {
                var description = process.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(description))
                    return description.Trim();
            }
            catch
            {
                // Elevated or protected process: no access to its modules
            }
            return process.ProcessName;
        }
        catch
        {
            return "Application";
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
