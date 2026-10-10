using System.Diagnostics;

namespace Vox.E2E.Tests;

/// <summary>
/// A test page from tests/pages open in its own Edge (or VOX_E2E_BROWSER) window, with a fresh
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

    public static BrowserSession Open(string page)
    {
        var path = Path.Combine(PagesDirectory, page);
        if (!File.Exists(path))
            throw new FileNotFoundException("No such test page", path);
        var profile = Path.Combine(Path.GetTempPath(), "vox-e2e-browser-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo(BrowserPath())
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

    private static string BrowserPath()
    {
        if (Environment.GetEnvironmentVariable("VOX_E2E_BROWSER") is { Length: > 0 } configured)
            return configured;
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            var edge = Path.Combine(Environment.GetFolderPath(root), "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(edge))
                return edge;
        }
        throw new FileNotFoundException("Microsoft Edge isn't installed; set VOX_E2E_BROWSER to a browser to use");
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
