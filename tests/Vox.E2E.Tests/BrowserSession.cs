using System.Diagnostics;

namespace Vox.E2E.Tests;

/// <summary>The browsers the end-to-end scenarios run in.</summary>
public enum Browser
{
    Edge,
    Chrome,
}

/// <summary>
/// A test page from tests/pages open in its own Edge or Chrome window (VOX_E2E_BROWSER overrides
/// Edge's path, VOX_E2E_CHROME Chrome's), with a fresh
/// profile so nothing from the user's browser (extensions, restored tabs, first-run pages) gets
/// in the way. Closed, with its profile, on dispose.
/// </summary>
public sealed class BrowserSession : IDisposable
{
    private readonly Process _process;
    private readonly string _profile;

    private BrowserSession(Process process, string profile)
    {
        _process = process;
        _profile = profile;
    }

    public static string PagesDirectory { get; } =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "pages"));

    public static BrowserSession Open(string page, Browser browser = Browser.Edge)
    {
        var path = Path.Combine(PagesDirectory, page);
        if (!File.Exists(path))
            throw new FileNotFoundException("No such test page", path);
        var profile = Path.Combine(Path.GetTempPath(), "vox-e2e-browser-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo(BrowserPath(browser) ?? throw new FileNotFoundException($"{browser} isn't installed"))
        {
            ArgumentList =
            {
                $"--user-data-dir={profile}", "--no-first-run", "--no-default-browser-check",
                "--disable-sync", "--new-window", "--force-renderer-accessibility",
                new Uri(path).AbsoluteUri,
            },
            UseShellExecute = false,
        };
        return new BrowserSession(Process.Start(start)!, profile);
    }

    /// <summary>True when <paramref name="browser"/> is installed (scenarios for a missing one are left out).</summary>
    public static bool IsInstalled(Browser browser) => BrowserPath(browser) is not null;

    private static string? BrowserPath(Browser browser)
    {
        var (variable, folder, exe) = browser switch
        {
            Browser.Chrome => ("VOX_E2E_CHROME", Path.Combine("Google", "Chrome", "Application"), "chrome.exe"),
            _ => ("VOX_E2E_BROWSER", Path.Combine("Microsoft", "Edge", "Application"), "msedge.exe"),
        };
        if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } configured)
            return configured;
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.LocalApplicationData })
        {
            var path = Path.Combine(Environment.GetFolderPath(root), folder, exe);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }
        catch (InvalidOperationException)
        {
        }
        _process.Dispose();
        try { Directory.Delete(_profile, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
