namespace Vox.Core.Lifecycle;

/// <summary>
/// Makes a portable copy of Vox (the Vox menu's "Create portable copy"): the program files, and
/// a <see cref="VoxPaths.PortableFolderName"/> folder beside them that makes the copy keep its
/// settings there. Optionally the user's own settings, keys, dictionaries and components go too.
/// </summary>
public static class PortableCopy
{
    // The user's files worth carrying (not logs or backups)
    private static readonly string[] UserFiles = ["settings.json", "keymap.json", "table-headers.json"];
    private static readonly string[] UserFolders = ["dictionaries"];

    /// <summary>
    /// Copies the Vox in <paramref name="programDirectory"/> to <paramref name="destination"/>.
    /// With <paramref name="userData"/>, the user's settings (and with <paramref name="components"/>
    /// the optional components) go into the copy's own folder. Returns how many files were copied.
    /// </summary>
    public static int Create(string programDirectory, string destination, string? userData = null, string? components = null)
    {
        var source = Path.GetFullPath(programDirectory);
        var target = Path.GetFullPath(destination);
        if (IsSameOrInside(target, source))
            throw new ArgumentException("A portable copy can't go inside Vox's own folder.", nameof(destination));

        int copied = 0;
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            // A portable copy's own user folder isn't part of the program
            if (relative.StartsWith(VoxPaths.PortableFolderName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;
            copied += CopyFile(file, Path.Combine(target, relative));
        }

        var portableData = Path.Combine(target, VoxPaths.PortableFolderName);
        Directory.CreateDirectory(portableData);
        if (userData is not null && Directory.Exists(userData))
        {
            foreach (var name in UserFiles)
            {
                var file = Path.Combine(userData, name);
                if (File.Exists(file))
                    copied += CopyFile(file, Path.Combine(portableData, name));
            }
            foreach (var name in UserFolders)
                copied += CopyFolder(Path.Combine(userData, name), Path.Combine(portableData, name));
        }
        if (components is not null)
            copied += CopyFolder(components, Path.Combine(portableData, "components"));
        return copied;
    }

    private static int CopyFolder(string from, string to)
    {
        if (!Directory.Exists(from))
            return 0;
        int copied = 0;
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            copied += CopyFile(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        return copied;
    }

    private static int CopyFile(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to, overwrite: true);
        return 1;
    }

    private static bool IsSameOrInside(string path, string folder)
    {
        var withSeparator = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        return string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase);
    }
}
