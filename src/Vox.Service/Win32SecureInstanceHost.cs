using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Vox.Core.Lifecycle;

namespace Vox.Service;

/// <summary>
/// Starts <c>Vox.App --secure</c> as SYSTEM in a user session on its Winlogon desktop: the
/// service's own token, duplicated with the target session id, through CreateProcessAsUser.
/// </summary>
public sealed class Win32SecureInstanceHost(string voxAppPath) : ISecureInstanceHost
{
    private const string SecureDesktop = @"WinSta0\Winlogon";

    public int? ActiveConsoleSession()
    {
        uint session = WTSGetActiveConsoleSessionId();
        return session == 0xFFFFFFFF ? null : (int)session;
    }

    public ISecureInstance Start(int sessionId)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ALL_ACCESS, out var serviceToken))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken");
        try
        {
            if (!DuplicateTokenEx(serviceToken, MAXIMUM_ALLOWED, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out var token))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx");
            try
            {
                int session = sessionId;
                if (!SetTokenInformation(token, TokenSessionId, ref session, sizeof(int)))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetTokenInformation");

                var startup = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = SecureDesktop };
                var commandLine = $"\"{voxAppPath}\" {RunPolicy.SecureFlag}";
                if (!CreateProcessAsUser(token, null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                        CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT, IntPtr.Zero, Path.GetDirectoryName(voxAppPath),
                        ref startup, out var info))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser");

                CloseHandle(info.hThread);
                CloseHandle(info.hProcess);
                return new Instance(sessionId, Process.GetProcessById(info.dwProcessId));
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(serviceToken);
        }
    }

    private sealed class Instance(int sessionId, Process process) : ISecureInstance
    {
        public int SessionId { get; } = sessionId;

        public bool HasExited
        {
            get
            {
                try { return process.HasExited; }
                catch (InvalidOperationException) { return true; }
            }
        }

        // The keyboard hook goes with the process; the instance keeps no state worth saving
        public void Stop()
        {
            try { process.Kill(); }
            catch (Exception) { /* already gone */ }
        }

        public void Dispose() => process.Dispose();
    }

    private const uint TOKEN_ALL_ACCESS = 0xF01FF;
    private const uint MAXIMUM_ALLOWED = 0x02000000;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;
    private const int TokenSessionId = 12;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attributes, int impersonationLevel,
        int tokenType, out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetTokenInformation(IntPtr token, int infoClass, ref int info, int length);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(IntPtr token, string? application, string commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment,
        string? currentDirectory, ref STARTUPINFO startup, out PROCESS_INFORMATION info);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
