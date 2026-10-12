using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Vox.Core.Localization;
using Vox.Core.Trial;

namespace Vox.Core.Lifecycle;

/// <summary>
/// What Vox.Watchdog did when Vox kept crashing: which screen reader it started (and whether it
/// started), after how many crashes. Kept in <c>fallback.json</c> in Vox's user folder until the
/// next Vox start says it (<see cref="TakeMessage"/>).
/// </summary>
public sealed record FallbackNotice(OtherScreenReader ScreenReader, bool Started, int Crashes, DateTimeOffset At)
{
    /// <summary>The file name under the user's Vox folder.</summary>
    public const string FileName = "fallback.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>What Vox says about it when it next starts.</summary>
    public string Message() => VoxStrings.Format(Started ? "Watchdog.FallbackNotice" : "Watchdog.FallbackFailedNotice",
        Crashes, TrialMode.Name(ScreenReader));

    public static FallbackNotice? Read(string? path)
    {
        if (path is null || !File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<FallbackNotice>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public bool Write(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The message about the last fallback, once: the notice at <paramref name="path"/> is
    /// removed when read. Null when there is none.
    /// </summary>
    public static string? TakeMessage(string? path)
    {
        var notice = Read(path);
        if (path is not null && File.Exists(path))
        {
            try { File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return notice?.Message();
    }
}

/// <summary>
/// Vox.Watchdog's last resort when Vox keeps crashing (its restart limit reached): rather than
/// leave the user without speech, it starts the screen reader trial mode stepped in for (from
/// <c>trial.json</c>), else Narrator, unless another screen reader is running already, and leaves
/// a <see cref="FallbackNotice"/> for the next Vox start to say why. A trial ends here, its
/// screen reader being back.
/// </summary>
public sealed class WatchdogFallback(IScreenReaderControl control, string? trialPath, string? noticePath, ILogger logger,
    TimeProvider? time = null)
{
    /// <summary>The screen reader to fall back to: the one trial mode recorded, else Narrator.</summary>
    public static OtherScreenReader Choose(TrialRecord? trial) => trial?.ReturnTo ?? OtherScreenReader.Narrator;

    /// <summary>Starts the fallback screen reader after <paramref name="crashes"/> crashes; what was done.</summary>
    public FallbackNotice Run(int crashes)
    {
        var trial = TrialRecord.Read(trialPath);
        var choice = Choose(trial);
        bool started;
        RunningScreenReader? running = null;
        try { running = control.FindRunning(); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not look for other screen readers"); }
        if (running is not null)
        {
            // Speech is there already: never a second screen reader on top of it
            choice = running.Kind;
            started = true;
        }
        else
        {
            started = control.Start(choice, trial?.ScreenReader == choice ? trial.ExecutablePath : null);
        }
        if (started && trial is not null)
            TrialRecord.Delete(trialPath);
        logger.Log(started ? LogLevel.Information : LogLevel.Error, "Vox crashed {Crashes} times; {ScreenReader} {Outcome}",
            crashes, choice, running is not null ? "was already running" : started ? "started" : "could not be started");

        var notice = new FallbackNotice(choice, started, crashes, (time ?? TimeProvider.System).GetUtcNow());
        if (noticePath is not null && !notice.Write(noticePath))
            logger.LogWarning("Could not leave the fallback notice");
        return notice;
    }
}
