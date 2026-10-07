using Microsoft.Extensions.Logging;

namespace Vox.Core.Lifecycle;

/// <summary>A secure-mode Vox started by Vox.Service in a session.</summary>
public interface ISecureInstance : IDisposable
{
    int SessionId { get; }
    bool HasExited { get; }
    void Stop();
}

/// <summary>Starts secure-mode Vox (as SYSTEM, on the session's Winlogon desktop) and finds the console session.</summary>
public interface ISecureInstanceHost
{
    /// <summary>The session attached to the physical console, or null when none (0xFFFFFFFF).</summary>
    int? ActiveConsoleSession();

    ISecureInstance Start(int sessionId);
}

/// <summary>
/// Vox.Service's logic: keeps exactly one secure-mode Vox running in the active console session,
/// so the sign-in, lock and UAC screens can be read. The instance itself stays silent until its
/// desktop has input (<see cref="SecureDesktopActivation"/>). Restarts it if it exits, at most
/// <see cref="MaxRestartsPerMinute"/> times a minute; moves it when the console session changes.
/// Call <see cref="Reconcile"/> on session changes and periodically.
/// </summary>
public sealed class SecureInstanceSupervisor : IDisposable
{
    public const int MaxRestartsPerMinute = 5;

    private readonly ISecureInstanceHost _host;
    private readonly ILogger _logger;
    private readonly RestartPolicy _restarts;
    private ISecureInstance? _instance;

    public SecureInstanceSupervisor(ISecureInstanceHost host, ILogger logger, Func<DateTimeOffset>? clock = null)
    {
        _host = host;
        _logger = logger;
        _restarts = new RestartPolicy(MaxRestartsPerMinute, TimeSpan.FromMinutes(1), clock);
    }

    /// <summary>The instance now running, if any.</summary>
    public ISecureInstance? Current => _instance;

    public void Reconcile()
    {
        var session = _host.ActiveConsoleSession();

        if (_instance is not null && (session != _instance.SessionId || _instance.HasExited))
        {
            if (!_instance.HasExited)
            {
                _logger.LogInformation("Console moved to session {Session}; stopping the secure instance in session {Old}", session, _instance.SessionId);
                _instance.Stop();
            }
            else
            {
                _logger.LogWarning("The secure instance in session {Session} exited", _instance.SessionId);
            }
            _instance.Dispose();
            _instance = null;
        }

        if (_instance is not null || session is not { } target)
            return;

        if (!_restarts.TryRestart())
            return; // crashing repeatedly: wait for the window to pass

        try
        {
            _instance = _host.Start(target);
            _logger.LogInformation("Started the secure instance in session {Session}", target);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start the secure instance in session {Session}", target);
        }
    }

    public void Dispose()
    {
        if (_instance is { HasExited: false })
            _instance.Stop();
        _instance?.Dispose();
        _instance = null;
    }
}

/// <summary>
/// When the secure-mode instance should be active: only while its own desktop (Winlogon: the
/// sign-in, lock, Ctrl+Alt+Del and UAC screens) has input. On the user's desktop it stays silent,
/// so it never doubles the user's own Vox.
/// </summary>
public static class SecureDesktopActivation
{
    public const string SecureDesktopName = "Winlogon";

    public static bool ShouldBeActive(string? inputDesktopName) =>
        string.Equals(inputDesktopName, SecureDesktopName, StringComparison.OrdinalIgnoreCase);
}
