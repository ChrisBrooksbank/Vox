using System.Globalization;

namespace Vox.Core.Switching;

/// <summary>A file of Vox's an import would write (the user keymap, a dictionary) and its new contents.</summary>
public sealed record PlannedFile(string Path, string Content);

/// <summary>
/// Writes the files an import planned, each atomically (a temporary file moved into place), after
/// copying a file it replaces to <c>&lt;name&gt;.&lt;time&gt;.bak</c> beside it so nothing is lost
/// without a copy. The backups are added to the report.
/// </summary>
public static class ImportFileWriter
{
    /// <summary>The backup name for <paramref name="path"/> made at <paramref name="time"/>: "keymap.json.20261012-202530.bak".</summary>
    public static string BackupPath(string path, DateTimeOffset time) =>
        $"{path}.{time.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.bak";

    /// <summary>Writes <paramref name="files"/>; an existing file is backed up first (not when its contents are unchanged).</summary>
    public static void Write(IEnumerable<PlannedFile> files, ImportReport report, DateTimeOffset time)
    {
        foreach (var file in files)
        {
            var directory = Path.GetDirectoryName(file.Path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            if (File.Exists(file.Path))
            {
                if (File.ReadAllText(file.Path) == file.Content)
                    continue;
                var backup = BackupPath(file.Path, time);
                File.Copy(file.Path, backup, overwrite: true);
                report.AddBackup(backup);
            }

            var temp = file.Path + ".tmp";
            File.WriteAllText(temp, file.Content);
            File.Move(temp, file.Path, overwrite: true);
        }
    }
}
