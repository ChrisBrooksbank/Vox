using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vox.Core.Trial;

/// <summary>
/// What trial mode stepped in for: the screen reader that was running (or null when none was) and
/// where it runs from, kept in <c>trial.json</c> in Vox's user folder while the trial lasts, so the
/// return key and Vox.Watchdog (after repeated crashes) start the right one again.
/// </summary>
public sealed record TrialRecord(OtherScreenReader? ScreenReader, string? ExecutablePath, DateTimeOffset Started)
{
    /// <summary>The file name under the user's Vox folder.</summary>
    public const string FileName = "trial.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The screen reader to go back to: the one that was running, else Narrator, which every Windows has.</summary>
    [JsonIgnore]
    public OtherScreenReader ReturnTo => ScreenReader ?? OtherScreenReader.Narrator;

    /// <summary>The record at <paramref name="path"/>, or null when there is none (or it can't be read).</summary>
    public static TrialRecord? Read(string? path)
    {
        if (path is null || !File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<TrialRecord>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Saves this record at <paramref name="path"/> (atomically); false when it couldn't be.</summary>
    public bool Write(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Ends the trial: removes the record.</summary>
    public static void Delete(string? path)
    {
        if (path is null)
            return;
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
