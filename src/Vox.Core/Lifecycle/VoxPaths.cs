namespace Vox.Core.Lifecycle;

/// <summary>
/// Where Vox keeps the user's files. Normally %APPDATA%\Vox (settings, keys, dictionaries, logs)
/// and %LOCALAPPDATA%\Vox\components (optional engines). A portable copy (a "userConfig" folder
/// beside the program, made by <see cref="PortableCopy"/>) keeps them all in that folder instead,
/// so it runs from a USB stick without touching the computer's profile.
/// </summary>
public static class VoxPaths
{
    public const string PortableFolderName = "userConfig";

    private static readonly Lazy<bool> Portable = new(() => IsPortableAt(AppContext.BaseDirectory));

    /// <summary>True when this Vox is a portable copy.</summary>
    public static bool IsPortable => Portable.Value;

    /// <summary>True when the Vox in <paramref name="programDirectory"/> is a portable copy.</summary>
    public static bool IsPortableAt(string programDirectory) =>
        Directory.Exists(Path.Combine(programDirectory, PortableFolderName));

    /// <summary>The user's Vox folder: settings, keymap, dictionaries, logs.</summary>
    public static string UserData => IsPortable
        ? Path.Combine(AppContext.BaseDirectory, PortableFolderName)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vox");

    /// <summary>Where optional components (eSpeak NG, MathCAT) are installed.</summary>
    public static string Components => IsPortable
        ? Path.Combine(UserData, "components")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vox", "components");
}
