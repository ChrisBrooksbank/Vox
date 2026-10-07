using System.Diagnostics;
using System.Speech.Synthesis;
using Microsoft.Extensions.Logging;
using Vox.Core.Lifecycle;

// Vox.Watchdog: starts Vox.App and restarts it if it crashes (at most 3 times a minute). If Vox
// keeps crashing it says so through SAPI directly, since there is no screen reader left to say it.
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

var supervisor = new VoxSupervisor(
    new ProcessLauncher(voxPath, voxArgs),
    new RestartPolicy(),
    () =>
    {
        using var synthesizer = new SpeechSynthesizer();
        synthesizer.Speak("Vox has stopped working and could not be restarted.");
    },
    logger);

return await supervisor.RunAsync(cts.Token);

sealed class ProcessLauncher(string path, string[] arguments) : IProcessLauncher
{
    public IRunningProcess Start()
    {
        var info = new ProcessStartInfo(path) { UseShellExecute = false };
        foreach (var argument in arguments)
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
