using Microsoft.Extensions.Logging;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

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
    ILogger<UpdateCommands> logger)
{
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
        Say("Checking for updates");
        var result = await checker.CheckAsync().ConfigureAwait(false);
        RememberCheck();
        if (result != UpdateCheckResult.Available)
        {
            Say(UpdateChecker.Describe(result, null));
            return;
        }
        var release = checker.Available!;
        if (!browse.BeginOwnDialog())
            return;
        bool install = false;
        try
        {
            install = await confirm.AskAsync("Update available",
                $"Vox {release.Version} is available. Download and install it now? Vox will close while it installs.").ConfigureAwait(false);
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
        Say($"Downloading Vox {release.Version}");
        try
        {
            if (await checker.InstallAsync(release, UpdateChecker.StartMsi).ConfigureAwait(false))
            {
                await speech.EnqueueAndWaitAsync(new Utterance("The installer is starting. Vox will close now.", SpeechPriority.Interrupt)).ConfigureAwait(false);
                exit();
            }
            else
            {
                Say("The download was damaged or altered, so it wasn't installed.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Update download failed");
            Say("The update couldn't be downloaded.");
        }
    }

    /// <summary>The quiet check at startup: at most daily, and only says something when there is an update.</summary>
    public async Task CheckQuietlyAsync(CancellationToken cancellationToken)
    {
        if (!checker.IsEnabled || !DueForCheck(DateTimeOffset.UtcNow))
            return;
        var result = await checker.CheckAsync(cancellationToken).ConfigureAwait(false);
        RememberCheck();
        if (result == UpdateCheckResult.Available)
            speech.Enqueue(new Utterance($"Vox {checker.Available!.Version} is available. Choose Check for updates in the Vox menu to install it.", SpeechPriority.Low));
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
