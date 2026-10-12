using Microsoft.Extensions.Logging;

namespace Vox.Core.Lifecycle;

/// <summary>Exit codes of Vox.App that the supervisor understands.</summary>
public static class VoxExitCodes
{
    /// <summary>The user quit Vox: don't restart.</summary>
    public const int Normal = 0;

    /// <summary>Vox stopped after an unexpected error: restart.</summary>
    public const int Fatal = 1;

    /// <summary>Another Vox instance is already running: don't restart.</summary>
    public const int AlreadyRunning = 2;

    /// <summary>The user asked for a restart with add-ons disabled (safe mode).</summary>
    public const int RestartSafe = 3;

    /// <summary>The user asked for a restart with add-ons enabled (leaving safe mode).</summary>
    public const int RestartNormal = 4;

    public static bool ShouldRestart(int exitCode) => exitCode is not (Normal or AlreadyRunning);

    /// <summary>A restart the user asked for, which never counts against the crash limit.</summary>
    public static bool IsRequestedRestart(int exitCode) => exitCode is RestartSafe or RestartNormal;
}

/// <summary>Starts a process (Vox.App in production).</summary>
public interface IProcessLauncher
{
    IRunningProcess Start();

    /// <summary>Starts it in safe mode (add-ons disabled) or normally.</summary>
    IRunningProcess Start(bool safeMode) => Start();
}

/// <summary>A started process the supervisor waits on.</summary>
public interface IRunningProcess : IDisposable
{
    Task<int> WaitForExitAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Allows at most <see cref="MaxRestarts"/> restarts within <see cref="Window"/>: a Vox that crashes
/// straight away again and again must not be restarted forever.
/// </summary>
public sealed class RestartPolicy
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly Queue<DateTimeOffset> _restarts = new();

    public RestartPolicy(int maxRestarts = 3, TimeSpan? window = null, Func<DateTimeOffset>? clock = null)
    {
        MaxRestarts = maxRestarts;
        Window = window ?? TimeSpan.FromMinutes(1);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public int MaxRestarts { get; }
    public TimeSpan Window { get; }

    /// <summary>Records a restart and returns true, or returns false if the limit is reached.</summary>
    public bool TryRestart()
    {
        var now = _clock();
        while (_restarts.Count > 0 && now - _restarts.Peek() >= Window)
            _restarts.Dequeue();
        if (_restarts.Count >= MaxRestarts)
            return false;
        _restarts.Enqueue(now);
        return true;
    }
}

/// <summary>
/// Runs Vox and restarts it after a crash, within the limits of a <see cref="RestartPolicy"/>.
/// When it gives up, <c>onGiveUp</c> tells the user (Vox.Watchdog speaks through SAPI directly).
/// The supervisor never holds a keyboard hook itself.
/// </summary>
public sealed class VoxSupervisor
{
    private readonly IProcessLauncher _launcher;
    private readonly RestartPolicy _policy;
    private readonly Action _onGiveUp;
    private readonly ILogger _logger;

    private readonly bool _startInSafeMode;

    /// <param name="startInSafeMode">Start Vox in safe mode (the watchdog was given --safe for it).</param>
    public VoxSupervisor(IProcessLauncher launcher, RestartPolicy policy, Action onGiveUp, ILogger logger, bool startInSafeMode = false)
    {
        _startInSafeMode = startInSafeMode;
        _launcher = launcher;
        _policy = policy;
        _onGiveUp = onGiveUp;
        _logger = logger;
    }

    /// <summary>Supervises until Vox exits normally, the limit is reached, or cancellation. Returns Vox's last exit code.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        bool safeMode = _startInSafeMode;
        while (true)
        {
            int exitCode;
            using (var process = _launcher.Start(safeMode))
            {
                try
                {
                    exitCode = await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return VoxExitCodes.Normal;
                }
            }

            if (VoxExitCodes.IsRequestedRestart(exitCode))
            {
                safeMode = exitCode == VoxExitCodes.RestartSafe;
                _logger.LogInformation("Vox asked to be restarted {Mode}", safeMode ? "in safe mode" : "normally");
                continue;
            }

            if (!VoxExitCodes.ShouldRestart(exitCode))
            {
                _logger.LogInformation("Vox exited with code {ExitCode}; not restarting", exitCode);
                return exitCode;
            }

            if (!_policy.TryRestart())
            {
                _logger.LogError("Vox exited with code {ExitCode} and has been restarted {Max} times in {Window}; giving up",
                    exitCode, _policy.MaxRestarts, _policy.Window);
                try { _onGiveUp(); }
                catch (Exception ex) { _logger.LogError(ex, "Could not tell the user that Vox stopped"); }
                return exitCode;
            }

            _logger.LogWarning("Vox exited with code {ExitCode}; restarting", exitCode);
        }
    }
}
