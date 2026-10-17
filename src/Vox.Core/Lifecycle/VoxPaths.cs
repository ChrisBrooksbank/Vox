namespace Vox.Core.Lifecycle;

/// <summary>
/// Where Vox keeps the user's files. Normally the user's choices in %APPDATA%\Vox (settings, keys,
/// dictionaries), which roams with a roaming profile, and what belongs to this computer in
/// %LOCALAPPDATA%\Vox (logs, optional engines in components), which doesn't. The SettingsLocation
/// policy can keep the user's choices in %LOCALAPPDATA%\Vox too (docs/enterprise.md). A portable
/// copy (a "userConfig" folder beside the program, made by <see cref="PortableCopy"/>) keeps them
/// all in that folder instead, so it runs from a USB stick without touching the computer's profile.
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

    private static volatile bool _userDataLocal;

    /// <summary>
    /// Keeps the user's folder on this computer (%LOCALAPPDATA%\Vox) instead of the roaming
    /// %APPDATA%\Vox: the SettingsLocation policy. Set once at startup, before anything reads
    /// <see cref="UserData"/>.
    /// </summary>
    public static void KeepUserDataLocal(bool local) => _userDataLocal = local;

    /// <summary>The user's Vox folder: settings, keymap, dictionaries.</summary>
    public static string UserData => IsPortable
        ? Path.Combine(AppContext.BaseDirectory, PortableFolderName)
        : Path.Combine(Environment.GetFolderPath(_userDataLocal
            ? Environment.SpecialFolder.LocalApplicationData
            : Environment.SpecialFolder.ApplicationData), "Vox");

    /// <summary>What belongs to this computer and never roams: logs, components.</summary>
    public static string LocalData => IsPortable
        ? UserData
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vox");

    private static volatile bool _machineComponents;

    /// <summary>
    /// Takes optional components only from <see cref="MachineComponents"/>, a folder only
    /// administrators can write: a Vox with UI access mustn't load code a user-level program could
    /// have put in the user's profile. Set once at startup, before anything reads <see cref="Components"/>.
    /// </summary>
    public static void RequireMachineComponents(bool required) => _machineComponents = required;

    /// <summary>The per-machine components folder, beside the program (in Program Files when installed).</summary>
    public static string MachineComponents => Path.Combine(AppContext.BaseDirectory, "components");

    /// <summary>Where optional components (eSpeak NG, MathCAT) are installed.</summary>
    public static string Components => _machineComponents && !IsPortable
        ? MachineComponents
        : Path.Combine(LocalData, "components");
}
