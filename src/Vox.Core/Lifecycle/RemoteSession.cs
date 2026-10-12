using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Vox.Core.Configuration;

namespace Vox.Core.Lifecycle;

/// <summary>What kind of Windows session Vox runs in.</summary>
public enum RemoteSessionKind
{
    /// <summary>At the computer itself (the console).</summary>
    Local,
    /// <summary>Remote Desktop (RDP), including Azure Virtual Desktop and Windows 365.</summary>
    RemoteDesktop,
    /// <summary>Citrix (ICA/HDX).</summary>
    Citrix,
    /// <summary>Windows says the session is remote, but not through which protocol.</summary>
    Other,
}

/// <summary>
/// The session Vox runs in. In a remote session Vox runs on the remote computer and reads it with
/// UIA there; the keys come through the remote-desktop client and the speech and sounds go back as
/// redirected audio (docs/remote-sessions.md). Some audio settings make no sense over that
/// connection, so <see cref="Adjust"/> turns them off while it lasts, without changing the settings.
/// </summary>
public sealed record RemoteSession(RemoteSessionKind Kind, string? ClientName = null)
{
    public static readonly RemoteSession Local = new(RemoteSessionKind.Local);

    public bool IsRemote => Kind != RemoteSessionKind.Local;

    /// <summary>"Remote Desktop", "Citrix", ...</summary>
    public string Name => Kind switch
    {
        RemoteSessionKind.RemoteDesktop => "Remote Desktop",
        RemoteSessionKind.Citrix => "Citrix",
        RemoteSessionKind.Other => "remote connection",
        _ => "this computer",
    };

    /// <summary>What Vox says once at startup in a remote session ("Remote session: Remote Desktop"); null at the computer.</summary>
    public string? Announcement => IsRemote ? $"Remote session: {Name}" : null;

    /// <summary>For the log.</summary>
    public string Describe() => IsRemote
        ? $"Remote session: {Name}" + (string.IsNullOrEmpty(ClientName) ? string.Empty : $" from {ClientName}")
        : "Local session";

    /// <summary>
    /// The settings as they work in this session. Remotely the audio device is the remote-desktop
    /// client's: keeping it awake would stream silence over the connection all the time (using
    /// bandwidth and delaying speech behind it), so it is off.
    /// </summary>
    public VoxSettings Adjust(VoxSettings settings) =>
        IsRemote && settings.KeepAudioDeviceAwake ? settings with { KeepAudioDeviceAwake = false } : settings;
}

/// <summary>What Windows says about the current session (behind an interface for tests).</summary>
public interface IRemoteSessionProbe
{
    /// <summary>GetSystemMetrics(SM_REMOTESESSION): the session is remote.</summary>
    bool IsRemoteSession { get; }

    /// <summary>WTSClientProtocolType: 0 the console, 1 ICA (Citrix), 2 RDP; null when unknown.</summary>
    int? ClientProtocolType { get; }

    /// <summary>The SESSIONNAME environment variable: "Console", "RDP-Tcp#3", "ICA-CGP#1", ...</summary>
    string? SessionName { get; }

    /// <summary>WTSClientName: the computer the user connects from, when remote.</summary>
    string? ClientName { get; }
}

/// <summary>Works out the kind of session from what Windows says.</summary>
public static class RemoteSessionDetector
{
    public const int ProtocolConsole = 0;
    public const int ProtocolIca = 1;
    public const int ProtocolRdp = 2;

    /// <summary>
    /// The session <paramref name="probe"/> describes. The protocol type decides when it is known;
    /// the session name ("RDP-Tcp#..", "ICA-...") otherwise; and SM_REMOTESESSION alone gives
    /// <see cref="RemoteSessionKind.Other"/>. A probe that fails counts as local.
    /// </summary>
    public static RemoteSession Detect(IRemoteSessionProbe probe, ILogger? logger = null)
    {
        try
        {
            var name = probe.SessionName ?? string.Empty;
            var kind = probe.ClientProtocolType switch
            {
                ProtocolIca => RemoteSessionKind.Citrix,
                ProtocolRdp => RemoteSessionKind.RemoteDesktop,
                _ when name.StartsWith("ICA", StringComparison.OrdinalIgnoreCase) => RemoteSessionKind.Citrix,
                _ when name.StartsWith("RDP", StringComparison.OrdinalIgnoreCase) => RemoteSessionKind.RemoteDesktop,
                _ when probe.IsRemoteSession => RemoteSessionKind.Other,
                _ => RemoteSessionKind.Local,
            };
            if (kind == RemoteSessionKind.Local)
                return RemoteSession.Local;
            var client = probe.ClientName;
            return new RemoteSession(kind, string.IsNullOrWhiteSpace(client) ? null : client.Trim());
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not tell whether this is a remote session");
            return RemoteSession.Local;
        }
    }
}

/// <summary>
/// The current session, detected at startup and again when the session is connected to (a user
/// at the console reconnecting from Remote Desktop, or back), see <see cref="Refresh"/>.
/// </summary>
public sealed class RemoteSessionMonitor
{
    private readonly IRemoteSessionProbe _probe;
    private readonly ILogger<RemoteSessionMonitor>? _logger;
    private readonly object _lock = new();
    private RemoteSession _current;

    public RemoteSessionMonitor(IRemoteSessionProbe probe, ILogger<RemoteSessionMonitor>? logger = null)
    {
        _probe = probe;
        _logger = logger;
        _current = RemoteSessionDetector.Detect(probe, logger);
        logger?.LogInformation("{Session}", _current.Describe());
    }

    public RemoteSession Current
    {
        get { lock (_lock) { return _current; } }
    }

    /// <summary>The session became remote, or local, or another kind of remote.</summary>
    public event EventHandler<RemoteSession>? Changed;

    /// <summary>Detects the session again; raises <see cref="Changed"/> and returns true when its kind changed.</summary>
    public bool Refresh()
    {
        var detected = RemoteSessionDetector.Detect(_probe, _logger);
        RemoteSession previous;
        lock (_lock)
        {
            previous = _current;
            _current = detected;
        }
        if (previous.Kind == detected.Kind)
            return false;
        _logger?.LogInformation("Session changed: {Session}", detected.Describe());
        Changed?.Invoke(this, detected);
        return true;
    }
}

/// <summary>Asks Windows (user32 and the Terminal Services API) about the current session.</summary>
public sealed class Win32RemoteSessionProbe : IRemoteSessionProbe
{
    private const int SM_REMOTESESSION = 0x1000;
    private const int WTS_CURRENT_SESSION = -1;
    private const int WTSClientName = 10;
    private const int WTSClientProtocolType = 16;

    public bool IsRemoteSession => OperatingSystem.IsWindows() && GetSystemMetrics(SM_REMOTESESSION) != 0;

    public int? ClientProtocolType =>
        Query(WTSClientProtocolType, (buffer, bytes) => bytes >= sizeof(ushort) ? Marshal.ReadInt16(buffer) : (int?)null);

    public string? SessionName => Environment.GetEnvironmentVariable("SESSIONNAME");

    public string? ClientName => Query(WTSClientName, (buffer, _) => Marshal.PtrToStringUni(buffer));

    private static T? Query<T>(int infoClass, Func<IntPtr, int, T?> read)
    {
        if (!OperatingSystem.IsWindows())
            return default;
        if (!WTSQuerySessionInformationW(IntPtr.Zero, WTS_CURRENT_SESSION, infoClass, out var buffer, out var bytes))
            return default;
        try
        {
            return buffer == IntPtr.Zero ? default : read(buffer, bytes);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformationW(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}
