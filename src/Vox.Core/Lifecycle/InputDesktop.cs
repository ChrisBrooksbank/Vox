using System.Runtime.InteropServices;

namespace Vox.Core.Lifecycle;

/// <summary>
/// The name of the desktop that receives input in this process's window station: "Default"
/// normally, "Winlogon" on the sign-in, lock, Ctrl+Alt+Del and UAC screens.
/// </summary>
public static class InputDesktop
{
    private const uint DESKTOP_READOBJECTS = 0x0001;
    private const int UOI_NAME = 2;

    /// <summary>The input desktop's name, or null if it can't be opened (not Windows, or no access).</summary>
    public static string? Name()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var desktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
        if (desktop == IntPtr.Zero)
            return null;
        try
        {
            var buffer = new char[256];
            return GetUserObjectInformation(desktop, UOI_NAME, buffer, buffer.Length * 2, out var needed)
                ? new string(buffer, 0, Math.Max(0, needed / 2 - 1))
                : null;
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }

    /// <summary>Waits until <paramref name="condition"/> holds for the input desktop's name, checking every 250 ms.</summary>
    public static async Task WaitUntilAsync(Func<string?, bool> condition, CancellationToken cancellationToken = default)
    {
        while (!condition(Name()))
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint desiredAccess);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(IntPtr obj, int index, [Out] char[] info, int length, out int lengthNeeded);

    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(IntPtr desktop);
}
