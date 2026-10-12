using Vox.Core.Configuration;
using Vox.Core.Input;

namespace Vox.Core.Switching;

/// <summary>Where an import reads the other screen readers' settings and writes Vox's.</summary>
/// <param name="NvdaFolder">NVDA's settings folder (<c>%APPDATA%\nvda</c>).</param>
/// <param name="JawsRoot">JAWS's per-version settings folders (<c>%APPDATA%\Freedom Scientific\JAWS</c>).</param>
/// <param name="VoxUserFolder">Vox's user folder: keymap and dictionaries.</param>
/// <param name="SettingsPath">Vox's settings file.</param>
public sealed record ImportLocations(string NvdaFolder, string JawsRoot, string VoxUserFolder, string SettingsPath)
{
    /// <summary>
    /// The locations in a Windows user profile folder (<c>C:\Users\name</c>): everything under its
    /// <c>AppData\Roaming</c>, Vox's in <c>Vox</c> — for importing another user's settings when deploying.
    /// </summary>
    public static ImportLocations ForProfile(string profileFolder)
    {
        string roaming = Path.Combine(profileFolder, "AppData", "Roaming");
        string vox = Path.Combine(roaming, "Vox");
        return new ImportLocations(Path.Combine(roaming, "nvda"), Path.Combine(roaming, "Freedom Scientific", "JAWS"),
            vox, Path.Combine(vox, "settings.json"));
    }

    /// <summary>The current user's: NVDA and JAWS under %APPDATA%, Vox's where it keeps them (<c>VoxPaths.UserData</c>).</summary>
    public static ImportLocations ForCurrentUser(string settingsPath) =>
        new(NvdaSettingsImporter.DefaultFolder, JawsSettingsImporter.DefaultRoot, Lifecycle.VoxPaths.UserData, settingsPath);
}

/// <summary>
/// Finds the other screen readers' settings and brings them over: <see cref="Prepare"/> reads them
/// into Vox's settings and planned files with a report, saving nothing, so the user can review it;
/// <see cref="Apply"/> then saves them, backing up every file of Vox's it replaces (settings.json
/// included). Used by the first-run wizard, the Vox menu and <c>Vox.App --import-settings</c>.
/// </summary>
public sealed class SettingsImport
{
    private readonly Func<string, IReadOnlyList<string>?> _voicesForEngine;
    private readonly Func<KeyboardLayout, IReadOnlyList<KeyBinding>> _layoutBindings;
    private readonly TimeProvider _time;

    /// <param name="voicesForEngine">The voices of a Vox speech engine by id, or null when it isn't installed.</param>
    /// <param name="layoutBindings">The bindings of a Vox keyboard layout (<see cref="KeyMap.LayoutBindings"/>).</param>
    public SettingsImport(ImportLocations locations, Func<string, IReadOnlyList<string>?> voicesForEngine,
        Func<KeyboardLayout, IReadOnlyList<KeyBinding>> layoutBindings, TimeProvider? time = null)
    {
        Locations = locations;
        _voicesForEngine = voicesForEngine;
        _layoutBindings = layoutBindings;
        _time = time ?? TimeProvider.System;
    }

    public ImportLocations Locations { get; }

    /// <summary>The screen readers whose settings are on this computer, NVDA first.</summary>
    public IReadOnlyList<ImportSource> FindSources()
    {
        var sources = new List<ImportSource>();
        try
        {
            string nvda = Locations.NvdaFolder;
            if (File.Exists(Path.Combine(nvda, NvdaSettingsImporter.ConfigFileName))
                || File.Exists(Path.Combine(nvda, NvdaGesturesImporter.FileName))
                || Directory.Exists(Path.Combine(nvda, NvdaDictionaryImporter.FolderName)))
                sources.Add(ImportSource.Nvda);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
        if (JawsSettingsImporter.FindSettingsFolder(Locations.JawsRoot) is not null)
            sources.Add(ImportSource.Jaws);
        return sources;
    }

    /// <summary>What importing from <paramref name="source"/> would do, or null when its settings aren't there.</summary>
    public SettingsImportResult? Prepare(ImportSource source, VoxSettings current)
    {
        if (!FindSources().Contains(source))
            return null;
        return source switch
        {
            ImportSource.Nvda => new NvdaSettingsImporter(_voicesForEngine)
                .ImportFolder(Locations.NvdaFolder, current, Locations.VoxUserFolder, _layoutBindings),
            _ => new JawsSettingsImporter(_voicesForEngine)
                .ImportFolder(JawsSettingsImporter.FindSettingsFolder(Locations.JawsRoot)!, current, Locations.VoxUserFolder, _layoutBindings),
        };
    }

    /// <summary>
    /// Saves a prepared import: the settings file is backed up and the settings saved through
    /// <paramref name="saveSettings"/> when they changed, then the keymap and dictionaries written
    /// (each replaced file backed up). The backups are added to the report.
    /// </summary>
    /// <param name="current">The settings the import was prepared from.</param>
    public void Apply(SettingsImportResult result, VoxSettings current, Action<VoxSettings> saveSettings)
    {
        var now = _time.GetUtcNow();
        bool settingsChanged = result.Settings != current;
        if (settingsChanged && File.Exists(Locations.SettingsPath))
        {
            string backup = ImportFileWriter.BackupPath(Locations.SettingsPath, now);
            File.Copy(Locations.SettingsPath, backup, overwrite: true);
            result.Report.AddBackup(backup);
        }
        ImportFileWriter.Write(result.Files, result.Report, now);
        if (settingsChanged)
            saveSettings(result.Settings);
    }

    /// <summary>The source named on the command line ("nvda", "jaws"), or null.</summary>
    public static ImportSource? ParseSource(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "nvda" => ImportSource.Nvda,
        "jaws" => ImportSource.Jaws,
        _ => null,
    };
}

/// <summary>
/// <c>Vox.App --import-settings nvda|jaws [--user-profile &lt;path&gt;]</c>: imports without asking,
/// for deployment, and writes to <paramref name="output"/> only what was imported (and how many
/// items were skipped, and the backups kept).
/// </summary>
public static class SettingsImportCli
{
    public const string Usage = "Usage: Vox.App --import-settings nvda|jaws [--user-profile <path>]";

    /// <summary>The exit code: 0 when imported, 1 when the settings weren't found or couldn't be saved.</summary>
    public static int Run(SettingsImport import, ImportSource source, Func<VoxSettings> load, Action<VoxSettings> save, TextWriter output)
    {
        var current = load();
        if (import.Prepare(source, current) is not { } result)
        {
            output.WriteLine($"No {source} settings found for this user.");
            return Lifecycle.VoxExitCodes.Fatal;
        }
        try
        {
            import.Apply(result, current, save);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            output.WriteLine($"Could not save the imported settings: {ex.Message}");
            return Lifecycle.VoxExitCodes.Fatal;
        }
        foreach (var item in result.Report.Imported.Where(i => i.Changed))
            output.WriteLine($"Imported {item.Target} = {item.Value} (from {item.Source})");
        output.WriteLine($"{result.Report.Imported.Count(i => i.Changed)} imported from {source}, {result.Report.Skipped.Count} skipped.");
        foreach (var backup in result.Report.Backups)
            output.WriteLine($"Backup: {backup}");
        return Lifecycle.VoxExitCodes.Normal;
    }
}
