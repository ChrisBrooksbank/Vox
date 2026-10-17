using System.Globalization;
using Vox.Core.Configuration;
using Vox.Sdk;

namespace Vox.Core.AddOns;

/// <summary>
/// Add-on settings as data: reads and changes the values kept in <see cref="VoxSettings.AddOnSettings"/>
/// (so profiles layer them and policy can lock them) and builds an add-on's page of the settings
/// dialog from the <see cref="AddOnSetting"/>s it declared. A stored value that no longer fits its
/// setting (a number out of range, a choice that was removed) reads as the default.
/// </summary>
public static class AddOnSettingsModel
{
    /// <summary>The value of <paramref name="setting"/> as text: the stored one if it is valid, else the default.</summary>
    public static string Value(VoxSettings settings, string addOnId, AddOnSetting setting)
    {
        if (settings.AddOnSettings.TryGetValue(addOnId, out var values)
            && values.TryGetValue(setting.Id, out var stored) && Normalize(setting, stored) is { } valid)
            return valid;
        return DefaultOf(setting);
    }

    /// <summary>The value of the setting called <paramref name="id"/> among <paramref name="declared"/>, or null for an unknown id.</summary>
    public static string? Value(VoxSettings settings, string addOnId, IEnumerable<AddOnSetting> declared, string id) =>
        declared.FirstOrDefault(s => s.Id == id) is { } setting ? Value(settings, addOnId, setting) : null;

    /// <summary><paramref name="settings"/> with <paramref name="setting"/> set to <paramref name="value"/> (unchanged when it doesn't fit).</summary>
    public static VoxSettings With(VoxSettings settings, string addOnId, AddOnSetting setting, string value)
    {
        if (Normalize(setting, value) is not { } valid)
            return settings;
        var all = settings.AddOnSettings.ToDictionary(e => e.Key, e => new Dictionary<string, string>(e.Value), StringComparer.Ordinal);
        if (!all.TryGetValue(addOnId, out var values))
            all[addOnId] = values = new Dictionary<string, string>(StringComparer.Ordinal);
        values[setting.Id] = valid;
        return settings with { AddOnSettings = all };
    }

    /// <summary>The add-on's page of the settings dialog.</summary>
    public static SettingsPage Page(string title, string addOnId, IReadOnlyList<AddOnSetting> declared) =>
        new(title, declared.Select(s => Field(addOnId, s)).ToList());

    private static SettingField Field(string addOnId, AddOnSetting setting) => setting switch
    {
        AddOnToggleSetting toggle => new ToggleField(toggle.Label,
            s => Value(s, addOnId, toggle) == "true",
            (s, on) => With(s, addOnId, toggle, on ? "true" : "false")),
        AddOnNumberSetting number => new NumberField(number.Label, number.Min, Math.Max(number.Min, number.Max), Math.Max(1, number.Step),
            s => int.Parse(Value(s, addOnId, number), CultureInfo.InvariantCulture),
            (s, v) => With(s, addOnId, number, v.ToString(CultureInfo.InvariantCulture))),
        AddOnChoiceSetting choice => new ChoiceField(choice.Label,
            choice.Choices.Select(c => new SettingChoice(c.Text, c.Value)).ToList(),
            s => Value(s, addOnId, choice),
            (s, v) => v is string text ? With(s, addOnId, choice, text) : s),
        _ => throw new ArgumentException($"Unknown kind of add-on setting {setting.GetType().Name}", nameof(setting)),
    };

    private static string DefaultOf(AddOnSetting setting) => setting switch
    {
        AddOnToggleSetting toggle => toggle.Default ? "true" : "false",
        AddOnNumberSetting number => Math.Clamp(number.Default, number.Min, Math.Max(number.Min, number.Max)).ToString(CultureInfo.InvariantCulture),
        AddOnChoiceSetting choice => choice.Default is { } d && choice.Choices.Any(c => c.Value == d)
            ? d : choice.Choices.FirstOrDefault()?.Value ?? string.Empty,
        _ => string.Empty,
    };

    /// <summary>The canonical text of <paramref name="value"/> for <paramref name="setting"/>, or null when it doesn't fit.</summary>
    private static string? Normalize(AddOnSetting setting, string value) => setting switch
    {
        AddOnToggleSetting => bool.TryParse(value, out var on) ? (on ? "true" : "false") : null,
        AddOnNumberSetting number => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            && n >= number.Min && n <= Math.Max(number.Min, number.Max) ? n.ToString(CultureInfo.InvariantCulture) : null,
        AddOnChoiceSetting choice => choice.Choices.Any(c => c.Value == value) ? value : null,
        _ => null,
    };
}
