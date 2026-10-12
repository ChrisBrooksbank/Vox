using Microsoft.Win32;

namespace Vox.Core.Enterprise;

/// <summary>Which registry hive a value is read from.</summary>
public enum RegistryRoot
{
    /// <summary>HKEY_LOCAL_MACHINE: the computer's policies and defaults.</summary>
    LocalMachine,
    /// <summary>HKEY_CURRENT_USER: the signed-in user's policies.</summary>
    CurrentUser,
}

/// <summary>Reads registry values (behind an interface, so policies can be faked in tests).</summary>
public interface IRegistryReader
{
    /// <summary>
    /// The values of <paramref name="key"/> by name (any case): strings, integers or string arrays.
    /// Empty when the key doesn't exist or can't be read.
    /// </summary>
    IReadOnlyDictionary<string, object> ReadValues(RegistryRoot root, string key);
}

/// <summary>The Windows registry (64-bit view, where Group Policy writes).</summary>
public sealed class Win32RegistryReader : IRegistryReader
{
    public IReadOnlyDictionary<string, object> ReadValues(RegistryRoot root, string key)
    {
        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows())
            return values;
        try
        {
            using var hive = RegistryKey.OpenBaseKey(
                root == RegistryRoot.LocalMachine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
                RegistryView.Registry64);
            using var opened = hive.OpenSubKey(key);
            if (opened is null)
                return values;
            foreach (var name in opened.GetValueNames())
            {
                // The unnamed (default) value isn't a setting
                if (name.Length > 0 && opened.GetValue(name) is { } value)
                    values[name] = value;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Unreadable: as if not set
        }
        return values;
    }
}
