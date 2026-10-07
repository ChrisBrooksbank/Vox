using System.Runtime.InteropServices;

namespace Vox.Core.Lifecycle;

/// <summary>
/// Whether this process has UI access (the uiAccess manifest flag, granted only to a signed exe
/// in a secure location): with it Vox can read and send input to elevated windows without
/// running elevated itself.
/// </summary>
public static class UIAccess
{
    private const int TokenUIAccess = 26;
    private const uint TOKEN_QUERY = 0x0008;

    /// <summary>True or false, or null if it can't be determined (not Windows).</summary>
    public static bool? IsGranted()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out var token))
            return null;
        try
        {
            return GetTokenInformation(token, TokenUIAccess, out var uiAccess, sizeof(int), out _)
                ? uiAccess != 0
                : null;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    /// <summary>The log message for the UI access state at startup.</summary>
    public static string Describe(bool? granted) => granted switch
    {
        true => "UI access granted: elevated windows can be read",
        false => "No UI access (unsigned or not installed in Program Files): elevated windows can't be read unless Vox runs as administrator",
        null => "UI access state unknown",
    };

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
