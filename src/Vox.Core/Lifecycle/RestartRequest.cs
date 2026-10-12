namespace Vox.Core.Lifecycle;

/// <summary>
/// A restart asked for from the Vox menu ("Restart with add-ons disabled", or enabled again from
/// safe mode). Vox stops; under Vox.Watchdog (which passes <see cref="SupervisedFlag"/>) it exits
/// with <see cref="VoxExitCodes.RestartSafe"/> or <see cref="VoxExitCodes.RestartNormal"/> and the
/// watchdog starts it again that way; on its own it starts the new instance itself
/// (<see cref="RestartedFlag"/>: wait for this one to exit) and exits normally.
/// </summary>
public sealed class RestartRequest
{
    /// <summary>Vox.Watchdog started this Vox and restarts it as its exit code asks.</summary>
    public const string SupervisedFlag = "--supervised";

    /// <summary>This Vox was started by the one before it, which may still be closing: wait for it.</summary>
    public const string RestartedFlag = "--restarted";

    private volatile object? _requested;

    /// <summary>Asks for a restart in safe mode (true) or normally (false) once Vox has stopped.</summary>
    public void Request(bool safeMode) => _requested = safeMode;

    /// <summary>The restart asked for: true safe mode, false normal, null none.</summary>
    public bool? SafeMode => _requested as bool?;

    /// <summary>The exit code that has the watchdog restart Vox this way.</summary>
    public static int ExitCode(bool safeMode) => safeMode ? VoxExitCodes.RestartSafe : VoxExitCodes.RestartNormal;

    /// <summary>The arguments for the new instance: these less the mode and restart flags, plus the new ones.</summary>
    public static IReadOnlyList<string> ArgumentsFor(IEnumerable<string> current, bool safeMode, bool supervised)
    {
        var args = current.Where(a => !IsFlag(a, RunPolicy.SafeFlag) && !IsFlag(a, RestartedFlag) && !IsFlag(a, SupervisedFlag)).ToList();
        if (safeMode)
            args.Add(RunPolicy.SafeFlag);
        if (supervised)
            args.Add(SupervisedFlag);
        else
            args.Add(RestartedFlag);
        return args;
    }

    public static bool HasFlag(IEnumerable<string> args, string flag) => args.Any(a => IsFlag(a, flag));

    private static bool IsFlag(string arg, string flag) => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase);
}
