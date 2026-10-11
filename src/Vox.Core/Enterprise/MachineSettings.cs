using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Vox.Core.Configuration;

namespace Vox.Core.Enterprise;

/// <summary>
/// The computer's default settings, set by an administrator: what a user starts with (between
/// Vox's built-in defaults and the user's own settings). Two places, the second winning:
/// <list type="bullet">
/// <item><c>%ProgramData%\Vox\machine-settings.json</c>, any settings, written like a user's settings.json;</item>
/// <item>the values under <c>HKLM\Software\Vox\Defaults</c>, named after the settings, which the installer
/// writes from its properties (UPDATECHANNEL, KEYBOARDLAYOUT, ...).</item>
/// </list>
/// A user who has changed a setting keeps their own value; to make everyone use one, use a policy
/// (<see cref="PolicySettings"/>).
/// </summary>
public static class MachineSettings
{
    /// <summary>The installer's defaults (HKLM).</summary>
    public const string DefaultsKey = @"Software\Vox\Defaults";

    /// <summary>The machine defaults file.</summary>
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Vox", "machine-settings.json");

    /// <summary>
    /// The machine defaults as settings-file JSON, or null when there are none. Unknown names and
    /// bad values are logged and left out.
    /// </summary>
    public static JsonObject? Read(IRegistryReader registry, string? filePath, ILogger? logger = null)
    {
        JsonObject? fromFile = null;
        if (filePath is not null && File.Exists(filePath))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(filePath)) is JsonObject document)
                {
                    // Written for an older Vox: brought up to date like a user's file
                    SettingsMigrations.Migrate(document);
                    var values = SettingValues.Convert(
                        document.Where(p => !string.Equals(p.Key, nameof(VoxSettings.SchemaVersion), StringComparison.OrdinalIgnoreCase))
                            .Select(p => new KeyValuePair<string, object>(p.Key, p.Value!)),
                        problem => logger?.LogWarning("Machine settings {Path}: {Problem}", filePath, problem));
                    fromFile = SettingValues.ToJson(values);
                }
                else
                {
                    logger?.LogWarning("Machine settings {Path} is not a JSON object; ignored", filePath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                logger?.LogWarning(ex, "Could not read the machine settings {Path}", filePath);
            }
        }

        var fromInstaller = SettingValues.ToJson(SettingValues.Convert(
            registry.ReadValues(RegistryRoot.LocalMachine, DefaultsKey)
                // The installer writes an empty value for a property that wasn't given
                .Where(v => v.Value is not string { Length: 0 }),
            problem => logger?.LogWarning("Machine settings HKLM\\{Key}: {Problem}", DefaultsKey, problem)));

        if (fromFile is null && fromInstaller.Count == 0)
            return null;
        var defaults = SettingValues.Overlay(fromFile, fromInstaller);
        logger?.LogInformation("Machine default settings: {Names}", string.Join(", ", defaults.Select(p => p.Key)));
        return defaults;
    }
}
