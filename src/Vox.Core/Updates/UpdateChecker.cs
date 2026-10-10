using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Vox.Core.Updates;

/// <summary>What a check found.</summary>
public enum UpdateCheckResult
{
    /// <summary>Checking is off, or not possible here (no release key, secure screen).</summary>
    Disabled,
    UpToDate,
    Available,
    /// <summary>The feed couldn't be fetched, or its signature didn't check out.</summary>
    Failed,
}

/// <summary>
/// Checks the signed update feed and installs an update on request: downloads the installer,
/// checks its SHA-256 against the signed feed, and starts it (Vox then closes so its files can
/// be replaced). Never installs anything on its own.
/// </summary>
public sealed class UpdateChecker(
    HttpClient http,
    Func<UpdateChannel> channel,
    string? publicKeyPem,
    Uri feedUrl,
    Version currentVersion,
    ILogger<UpdateChecker> logger)
{
    /// <summary>The signed feed of every release (feed.json.sig beside it).</summary>
    public static readonly Uri DefaultFeedUrl = new("https://raw.githubusercontent.com/ChrisBrooksbank/Vox/master/updates/feed.json");

    /// <summary>The release found by the last check, if newer than this Vox.</summary>
    public UpdateRelease? Available { get; private set; }

    public bool IsEnabled => publicKeyPem is not null && channel() != UpdateChannel.Off;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            return UpdateCheckResult.Disabled;
        try
        {
            var feed = await http.GetByteArrayAsync(feedUrl, cancellationToken).ConfigureAwait(false);
            var signature = await http.GetStringAsync(new Uri(feedUrl + ".sig"), cancellationToken).ConfigureAwait(false);
            if (!UpdateFeed.IsSigned(feed, signature, publicKeyPem!))
            {
                logger.LogWarning("The update feed's signature doesn't match the release key; ignored");
                return UpdateCheckResult.Failed;
            }
            Available = UpdateFeed.Newer(UpdateFeed.Parse(System.Text.Encoding.UTF8.GetString(feed)), currentVersion, channel());
            return Available is null ? UpdateCheckResult.UpToDate : UpdateCheckResult.Available;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException)
        {
            logger.LogInformation(ex, "Update check failed");
            return UpdateCheckResult.Failed;
        }
    }

    /// <summary>
    /// Downloads <paramref name="release"/>'s installer, checks it, and starts it. Returns false
    /// (deleting the download) when its checksum isn't the signed feed's.
    /// </summary>
    public async Task<bool> InstallAsync(UpdateRelease release, Action<string> startInstaller, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Path.GetTempPath(), $"Vox-{release.Version}.msi");
        using (var response = await http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(path);
            await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }
        bool valid;
        await using (var file = File.OpenRead(path))
            valid = UpdateFeed.HasChecksum(file, release.Sha256);
        if (!valid)
        {
            logger.LogWarning("Downloaded installer for {Version} doesn't match the signed checksum; deleted", release.Version);
            File.Delete(path);
            return false;
        }
        startInstaller(path);
        return true;
    }

    /// <summary>Starts an MSI with its own progress window (Windows Installer asks for elevation).</summary>
    public static void StartMsi(string path) =>
        Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{path}\" /passive") { UseShellExecute = true })?.Dispose();

    /// <summary>The release key shipped beside Vox (assets/config/update-key.pem), or null: no updates.</summary>
    public static string? ReadPublicKey(string configDirectory)
    {
        var path = Path.Combine(configDirectory, "update-key.pem");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>What to say about a check.</summary>
    public static string Describe(UpdateCheckResult result, UpdateRelease? release) => result switch
    {
        UpdateCheckResult.Available => $"Vox {release!.Version} is available.",
        UpdateCheckResult.UpToDate => "Vox is up to date.",
        UpdateCheckResult.Failed => "Couldn't check for updates.",
        _ => "Checking for updates is off.",
    };
}
