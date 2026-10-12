using Microsoft.Extensions.Logging;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Localization;

namespace Vox.Core.Updates;

/// <summary>
/// "Check for updates" (Vox menu) and the quiet daily check at startup. A check says what it
/// found; installing always asks first, then Vox closes so the installer can replace it.
/// Pipeline thread for <see cref="TryHandle"/>.
/// </summary>
public sealed class UpdateCommands(
    UpdateChecker checker,
    IConfirmPresenter confirm,
    BrowseModeController browse,
    IEventSink pipeline,
    SpeechQueue speech,
    Action exit,
    string? lastCheckFile,
    ILogger<UpdateCommands> logger,
    Action<string>? startInstaller = null)
{
    /// <summary>How long the goodbye may take before Vox closes for the installer anyway.</summary>
    public static readonly TimeSpan GoodbyeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>How often the quiet check at startup runs.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    public bool TryHandle(NavigationCommand command)
    {
        if (command != NavigationCommand.CheckForUpdates)
            return false;
        _ = CheckNowAsync();
        return true;
    }

    private async Task CheckNowAsync()
    {
        Say(VoxStrings.Get("Update.Checking"));
        var result = await checker.CheckAsync().ConfigureAwait(false);
        RememberCheck();
        if (result != UpdateCheckResult.Available)
        {
            Say(UpdateChecker.Describe(result, null));
            return;
        }
        var release = checker.Available!;
        // BeginOwnDialog is pipeline-thread only, and this continues on the thread pool
        pipeline.Post(new PipelineWorkEvent(DateTimeOffset.UtcNow, () => AskToInstall(release)));
    }

    /// <summary>Asks whether to install <paramref name="release"/>. Pipeline thread.</summary>
    private void AskToInstall(UpdateRelease release)
    {
        if (!browse.BeginOwnDialog())
        {
            // Another of Vox's dialogs is open: say what was found rather than nothing
            Say(VoxStrings.Format("Update.AvailableCloseDialog", release.Version));
            return;
        }
        _ = AskAndInstallAsync(release);
    }

    private async Task AskAndInstallAsync(UpdateRelease release)
    {
        bool install = false;
        try
        {
            install = await confirm.AskAsync(VoxStrings.Get("Update.AvailableTitle"),
                VoxStrings.Format("Update.InstallQuestion", release.Version)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Update question failed");
        }
        pipeline.Post(new VoxDialogClosedEvent(DateTimeOffset.UtcNow));
        if (install)
            await InstallAsync(release).ConfigureAwait(false);
    }

    private async Task InstallAsync(UpdateRelease release)
    {
        Say(VoxStrings.Format("Update.Downloading", release.Version));
        bool started;
        try
        {
            started = await checker.InstallAsync(release, startInstaller ?? UpdateChecker.StartMsi).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Update download failed");
            Say(VoxStrings.Get("Update.DownloadFailed"));
            return;
        }
        if (!started)
        {
            Say(VoxStrings.Get("Update.DownloadDamaged"));
            return;
        }

        // The installer is running and needs Vox's files: close whatever happens to the goodbye
        // (focus moving to the installer interrupts it; paused speech or sleep mode drops it)
        try
        {
            await speech.EnqueueAndWaitAsync(new Utterance(VoxStrings.Get("Update.InstallerStarting"), SpeechPriority.Interrupt))
                .WaitAsync(GoodbyeTimeout).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
        }
        exit();
    }

    /// <summary>The quiet check at startup: at most daily, and only says something when there is an update.</summary>
    public async Task CheckQuietlyAsync(CancellationToken cancellationToken)
    {
        if (!checker.IsEnabled || !DueForCheck(DateTimeOffset.UtcNow))
            return;
        var result = await checker.CheckAsync(cancellationToken).ConfigureAwait(false);
        RememberCheck();
        if (result == UpdateCheckResult.Available)
            speech.Enqueue(new Utterance(VoxStrings.Format("Update.AvailableDaily", checker.Available!.Version), SpeechPriority.Low));
    }

    /// <summary>Whether a day has passed since the last check (the time is kept in <c>lastCheckFile</c>).</summary>
    public bool DueForCheck(DateTimeOffset now)
    {
        if (lastCheckFile is null)
            return false;
        try
        {
            return !File.Exists(lastCheckFile)
                || !DateTimeOffset.TryParse(File.ReadAllText(lastCheckFile), out var last)
                || now - last >= CheckInterval;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private void RememberCheck()
    {
        if (lastCheckFile is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lastCheckFile)!);
            File.WriteAllText(lastCheckFile, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not remember the update check");
        }
    }

    private void Say(string text) => speech.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
}
