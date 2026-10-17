namespace Vox.Core.Lifecycle;

/// <summary>
/// What limits the secure-screen instance, which runs as SYSTEM on the sign-in screen
/// (docs/security.md weaknesses 3 and 11).
/// </summary>
public static class SecureInstanceHardening
{
    /// <summary>
    /// The single-instance mutex of the secure instance. In the global namespace, which a program
    /// without SeCreateGlobalPrivilege (any ordinary user's) can't create, so none can squat on it
    /// and stop Vox starting on the sign-in screen.
    /// </summary>
    public const string MutexName = @"Global\Vox.ScreenReader.Secure";

    /// <summary>
    /// Privileges removed from the SYSTEM token before the secure instance starts. Left are the ones
    /// a screen reader on the secure desktop uses (SeChangeNotify, and SeCreateGlobal for the mutex
    /// above). It never impersonates, so SeImpersonate goes too.
    /// </summary>
    public static readonly IReadOnlyList<string> RemovedPrivileges =
    [
        "SeAssignPrimaryTokenPrivilege",
        "SeBackupPrivilege",
        "SeCreatePagefilePrivilege",
        "SeCreatePermanentPrivilege",
        "SeCreateTokenPrivilege",
        "SeDebugPrivilege",
        "SeImpersonatePrivilege",
        "SeLoadDriverPrivilege",
        "SeLockMemoryPrivilege",
        "SeManageVolumePrivilege",
        "SeRelabelPrivilege",
        "SeRestorePrivilege",
        "SeSecurityPrivilege",
        "SeSystemEnvironmentPrivilege",
        "SeSystemtimePrivilege",
        "SeTakeOwnershipPrivilege",
        "SeTcbPrivilege",
        "SeTrustedCredManAccessPrivilege",
        "SeUndockPrivilege",
    ];
}
