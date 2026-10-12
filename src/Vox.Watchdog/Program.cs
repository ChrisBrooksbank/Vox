using System.Diagnostics;
using System.Speech.Synthesis;
using Microsoft.Extensions.Logging;
using Vox.Core.Lifecycle;

// Vox.Watchdog: starts Vox.App and restarts it if it crashes (at most 3 times a minute). If Vox
// keeps crashing it says so through SAPI directly, since there is no screen reader left to say it,
// and starts Narrator (or the screen reader trial mode stepped in for) so the user isn't left
// without speech; the next Vox start says why (WatchdogFallback).
// Usage: Vox.Watchdog [path to Vox.App.exe] [-- arguments for Vox.App]

var separator = Array.IndexOf(args, "--");
var ownArgs = separator < 0 ? args : args[..separator];
var voxArgs = separator < 0 ? [] : args[(separator + 1)..];
var voxPath = ownArgs.Length > 0 ? ownArgs[0] : Path.Combine(AppContext.BaseDirectory, "Vox.App.exe");

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger = loggerFactory.CreateLogger("Vox.Watchdog");

if (!File.Exists(voxPath))
{
    logger.LogError("Vox.App not found at {Path}", voxPath);
    return 1;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var restartPolicy = new RestartPolicy();
var fallback = new WatchdogFallback(
    new Vox.Core.Trial.WindowsScreenReaderControl(logger),
    Path.Combine(VoxPaths.UserData, Vox.Core.Trial.TrialRecord.FileName),
    Path.Combine(VoxPaths.UserData, FallbackNotice.FileName),
    logger);
var supervisor = new VoxSupervisor(
    new ProcessLauncher(voxPath, voxArgs),
    restartPolicy,
    () =>
    {
        var choice = WatchdogFallback.Choose(Vox.Core.Trial.TrialRecord.Read(Path.Combine(VoxPaths.UserData, Vox.Core.Trial.TrialRecord.FileName)));
        using (var synthesizer = new SpeechSynthesizer())
            synthesizer.Speak(Vox.Core.Localization.VoxStrings.Format("Watchdog.StartingFallback", Vox.Core.Trial.TrialMode.Name(choice)));
        // The first start, then each restart allowed, then the one that gave up
        fallback.Run(restartPolicy.MaxRestarts + 1);
    },
    logger,
    startInSafeMode: RestartRequest.HasFlag(voxArgs, RunPolicy.SafeFlag));

return await supervisor.RunAsync(cts.Token);

sealed class ProcessLauncher(string path, string[] arguments) : IProcessLauncher
{
    public IRunningProcess Start() => Start(safeMode: false);

    // Vox.App is told it is supervised, so a restart it is asked for goes through its exit code
    public IRunningProcess Start(bool safeMode)
    {
        // Vox.App is a console program (its tools print); started by the watchdog it gets no window
        var info = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in RestartRequest.ArgumentsFor(arguments, safeMode, supervised: true))
            info.ArgumentList.Add(argument);
        return new RunningProcess(Process.Start(info) ?? throw new InvalidOperationException($"Could not start {path}"));
    }
}

sealed class RunningProcess(Process process) : IRunningProcess
{
    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    public void Dispose() => process.Dispose();
}
