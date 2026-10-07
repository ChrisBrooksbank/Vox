using Microsoft.Extensions.Logging;

namespace Vox.Core.Input;

/// <summary>
/// Makes sure a crash never leaves the user's keyboard captured. On an unhandled exception (and
/// at process exit) the low-level keyboard hook is uninstalled before anything else: a process
/// kept alive by a crash dialog with a frozen hook would delay or swallow every key the user
/// presses, system-wide.
/// </summary>
public sealed class HookSafetyNet : IDisposable
{
    private readonly IKeyboardHook _hook;
    private readonly ILogger<HookSafetyNet> _logger;
    private int _released;
    private bool _registered;

    public HookSafetyNet(IKeyboardHook hook, ILogger<HookSafetyNet> logger)
    {
        _hook = hook;
        _logger = logger;
    }

    /// <summary>Whether the hook has been released by this safety net.</summary>
    public bool HookReleased => Volatile.Read(ref _released) == 1;

    /// <summary>Subscribes to the process's unhandled-exception and exit events.</summary>
    public void Register()
    {
        if (_registered) return;
        _registered = true;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e) =>
        HandleFatalError(e.ExceptionObject as Exception);

    private void OnProcessExit(object? sender, EventArgs e) => ReleaseHook();

    /// <summary>
    /// Vox is going down because of <paramref name="exception"/>: release the keyboard first, then log.
    /// </summary>
    public void HandleFatalError(Exception? exception)
    {
        ReleaseHook();
        try
        {
            _logger.LogCritical(exception, "Unhandled exception; keyboard hook released");
        }
        catch
        {
            // Logging must not stop the process from going down
        }
    }

    /// <summary>Uninstalls the keyboard hook once; never throws.</summary>
    public void ReleaseHook()
    {
        if (Interlocked.Exchange(ref _released, 1) == 1)
            return;
        try
        {
            _hook.Uninstall();
        }
        catch
        {
            // Nothing more can be done; the OS removes the hook when the process ends
        }
    }

    public void Dispose()
    {
        if (!_registered) return;
        _registered = false;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    }
}
