using System.Diagnostics;

namespace Vox.E2E.Tests;

/// <summary>
/// Notepad with a file of its own in a new temporary folder ("Vox notepad test.txt"). Closed on
/// dispose; the scenario saves first, so closing never asks. With the Windows 11 Notepad the file
/// may open as a tab in a Notepad window that is already open: close Notepad before a run.
/// </summary>
public sealed class NotepadSession : IAsyncDisposable
{
    public const string FileTitle = "Vox notepad test";

    private readonly string _folder;

    private NotepadSession(string folder, IntPtr window)
    {
        _folder = folder;
        Window = window;
    }

    public IntPtr Window { get; }

    public static async Task<NotepadSession> OpenAsync(string text)
    {
        var folder = Path.Combine(Path.GetTempPath(), "vox-e2e-notepad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileTitle + ".txt");
        await File.WriteAllTextAsync(path, text);
        // On Windows 11 this starts the packaged Notepad and may exit at once
        Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path }, UseShellExecute = false })?.Dispose();
        var window = await DesktopWindows.WaitForAsync(IsOurs, TimeSpan.FromSeconds(20), FileTitle + " in Notepad");
        await DesktopWindows.EnsureForegroundAsync(window.Handle, TimeSpan.FromSeconds(10));
        return new NotepadSession(folder, window.Handle);
    }

    private static bool IsOurs(DesktopWindows.WindowInfo window) =>
        window.Title.Contains(FileTitle, StringComparison.OrdinalIgnoreCase) && window.Title.Contains("Notepad", StringComparison.OrdinalIgnoreCase);

    public async ValueTask DisposeAsync()
    {
        await DesktopWindows.CloseAllAsync(IsOurs, TimeSpan.FromSeconds(5));
        TryDelete(_folder);
    }

    internal static void TryDelete(string folder)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500); // Explorer or Notepad still has it open
            }
        }
    }
}

/// <summary>
/// A File Explorer window on a new temporary folder "Vox explorer test" holding a "Delta folder"
/// and three text files (Alpha, Beta and Gamma notes). Closed, and the folder deleted, on dispose.
/// </summary>
public sealed class ExplorerSession : IAsyncDisposable
{
    public const string FolderName = "Vox explorer test";
    public const string SubfolderName = "Delta folder";
    public static readonly string[] FileNames = ["Alpha notes", "Beta notes", "Gamma notes"];

    private readonly string _root;

    private ExplorerSession(string root, IntPtr window)
    {
        _root = root;
        Window = window;
    }

    public IntPtr Window { get; }

    public static async Task<ExplorerSession> OpenAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "vox-e2e-explorer-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, FolderName);
        Directory.CreateDirectory(Path.Combine(folder, SubfolderName));
        foreach (var name in FileNames)
            await File.WriteAllTextAsync(Path.Combine(folder, name + ".txt"), $"{name} for the Vox end-to-end tests.");
        // explorer.exe hands the folder to the running shell and exits
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = false })?.Dispose();
        var window = await DesktopWindows.WaitForAsync(IsOurs, TimeSpan.FromSeconds(20), FolderName + " in File Explorer");
        await DesktopWindows.EnsureForegroundAsync(window.Handle, TimeSpan.FromSeconds(10));
        return new ExplorerSession(root, window.Handle);
    }

    // Its title follows the folder shown, the subfolder included
    private static bool IsOurs(DesktopWindows.WindowInfo window) =>
        window.ClassName == "CabinetWClass"
        && (window.Title.StartsWith(FolderName, StringComparison.OrdinalIgnoreCase)
            || window.Title.StartsWith(SubfolderName, StringComparison.OrdinalIgnoreCase));

    public async ValueTask DisposeAsync()
    {
        await DesktopWindows.CloseAllAsync(IsOurs, TimeSpan.FromSeconds(5));
        NotepadSession.TryDelete(_root);
    }
}

/// <summary>
/// The Settings app opened on a page (an ms-settings: address). Closed on dispose (if Settings was
/// already open it is navigated, and closed all the same).
/// </summary>
public sealed class SettingsSession : IAsyncDisposable
{
    private SettingsSession(IntPtr window) => Window = window;

    public IntPtr Window { get; }

    public static async Task<SettingsSession> OpenAsync(string page)
    {
        Process.Start(new ProcessStartInfo(page) { UseShellExecute = true })?.Dispose();
        var window = await DesktopWindows.WaitForAsync(IsSettings, TimeSpan.FromSeconds(20), "the Settings app");
        await DesktopWindows.EnsureForegroundAsync(window.Handle, TimeSpan.FromSeconds(10));
        return new SettingsSession(window.Handle);
    }

    private static bool IsSettings(DesktopWindows.WindowInfo window) =>
        window.ClassName == "ApplicationFrameWindow" && window.Title == "Settings";

    public async ValueTask DisposeAsync() => await DesktopWindows.CloseAllAsync(IsSettings, TimeSpan.FromSeconds(5));
}
