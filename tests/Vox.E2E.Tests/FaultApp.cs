using System.Collections.Concurrent;
using System.Diagnostics;

namespace Vox.E2E.Tests;

/// <summary>
/// tests/Vox.FaultTestApp running in its own process (UIA treats a hang in Vox's own process
/// differently, and Vox never reports itself as not responding): a "Vox fault test" window with
/// Before, Faulty button, After and a Notes field, whose faulty button's provider hangs or throws
/// on command.
/// </summary>
public sealed class FaultApp : IDisposable
{
    public const string Title = "Vox fault test";

    private readonly Process _process;
    private readonly ConcurrentQueue<string> _lines = new();

    private FaultApp(Process process)
    {
        _process = process;
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { } line)
                _lines.Enqueue(line);
        };
        process.BeginOutputReadLine();
    }

    public int ProcessId => _process.Id;

    public IntPtr Window { get; private set; }

    /// <summary>Starts the app and waits until its window is in front.</summary>
    public static async Task<FaultApp> StartAsync()
    {
        var path = ExecutablePath();
        var start = path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo("dotnet") { ArgumentList = { path } }
            : new ProcessStartInfo(path);
        start.UseShellExecute = false;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        var app = new FaultApp(Process.Start(start) ?? throw new InvalidOperationException("The fault test app didn't start"));
        try
        {
            await app.WaitForLineAsync(l => l == "ready", TimeSpan.FromSeconds(20));
            var window = await DesktopWindows.WaitForAsync(w => w.ProcessId == app.ProcessId && w.Title == Title,
                TimeSpan.FromSeconds(10), Title);
            app.Window = window.Handle;
            await DesktopWindows.EnsureForegroundAsync(app.Window, TimeSpan.FromSeconds(10));
            return app;
        }
        catch
        {
            app.Dispose();
            throw;
        }
    }

    /// <summary>Makes the faulty button's provider block every read until <see cref="Release"/> (at most 60 s).</summary>
    public Task HangAsync() => CommandAsync("hang");

    /// <summary>Makes the faulty button's provider throw on every read.</summary>
    public Task ThrowAsync() => CommandAsync("throw");

    /// <summary>Back to normal; reads blocked by a hang return.</summary>
    public Task ReleaseAsync() => CommandAsync("release");

    /// <summary>What the Notes field holds (only while the app isn't hung).</summary>
    public async Task<string> NotesAsync()
    {
        _process.StandardInput.WriteLine("text");
        var line = await WaitForLineAsync(l => l.StartsWith("text:", StringComparison.Ordinal), TimeSpan.FromSeconds(10));
        return line["text:".Length..];
    }

    private async Task CommandAsync(string command)
    {
        _process.StandardInput.WriteLine(command);
        await WaitForLineAsync(l => l == "ok:" + command, TimeSpan.FromSeconds(10));
    }

    private async Task<string> WaitForLineAsync(Func<string, bool> match, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            while (_lines.TryDequeue(out var line))
            {
                if (match(line))
                    return line;
            }
            if (_process.HasExited)
                throw new InvalidOperationException($"The fault test app exited (code {_process.ExitCode})");
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The fault test app didn't answer");
            await Task.Delay(50);
        }
    }

    /// <summary>
    /// The app's own build output, beside this project's: tests/Vox.FaultTestApp/bin/&lt;configuration&gt;/&lt;framework&gt;/.
    /// </summary>
    private static string ExecutablePath()
    {
        var framework = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var configuration = framework.Parent!;
        var tests = configuration.Parent!.Parent!.Parent!;
        var output = Path.Combine(tests.FullName, "Vox.FaultTestApp", "bin", configuration.Name, framework.Name);
        foreach (var candidate in new[] { "Vox.FaultTestApp.exe", "Vox.FaultTestApp.dll" })
        {
            var path = Path.Combine(output, candidate);
            if (File.Exists(path))
                return path;
        }
        throw new FileNotFoundException("Build tests/Vox.FaultTestApp first (building Vox.E2E.Tests does)", output);
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.WriteLine("quit");
                if (!_process.WaitForExit(5000))
                    _process.Kill();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
        }
        _process.Dispose();
    }
}
