using System.Collections.Concurrent;
using System.Diagnostics;

namespace Vox.Core.Accessibility;

/// <summary>
/// The Windows shell's processes: the taskbar, Alt+Tab and Win+Tab (explorer), the Start menu and
/// its search. Their announcements matter even though the shell is rarely the foreground app.
/// </summary>
public static class ShellProcesses
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "SearchHost", "SearchApp", "StartMenuExperienceHost", "ShellExperienceHost",
        "ShellHost",
    };

    private static readonly ConcurrentDictionary<int, bool> ByProcessId = new();

    /// <summary>Whether a process name is one of the shell's.</summary>
    public static bool IsShellName(string processName) => Names.Contains(processName);

    /// <summary>Whether a process is one of the shell's (cached; process ids are reused rarely enough).</summary>
    public static bool IsShell(int processId)
    {
        if (processId <= 0)
            return false;
        return ByProcessId.GetOrAdd(processId, static pid =>
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return IsShellName(process.ProcessName);
            }
            catch
            {
                return false;
            }
        });
    }
}
