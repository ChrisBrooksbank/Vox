using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Vox.Core.Configuration;

namespace Vox.Core.Enterprise;

/// <summary>
/// Settings named from outside Vox (registry values set by the installer or Group Policy, the
/// machine defaults file): finds the <see cref="VoxSettings"/> property a name means and turns a
/// value into one of its type, so a bad value is dropped on its own instead of spoiling the rest.
/// </summary>
public static class SettingValues
{
    private static readonly Dictionary<string, PropertyInfo> Properties = typeof(VoxSettings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite)
        .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>As the settings file is read (<see cref="SettingsManager"/>).</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The names of every setting.</summary>
    public static IEnumerable<string> Names => Properties.Values.Select(p => p.Name);

    /// <summary>The setting called <paramref name="name"/> (any case), or null.</summary>
    public static PropertyInfo? Find(string name) => Properties.GetValueOrDefault(name);

    /// <summary>
    /// <paramref name="raw"/> as a value of <paramref name="setting"/>'s type: a registry DWORD or
    /// "1"/"true" for an on/off setting, a number or its digits for a number, an enum's name or
    /// number, a string, or JSON for lists and maps (a multi-string value for a list). False when it
    /// isn't one.
    /// </summary>
    public static bool TryConvert(PropertyInfo setting, object? raw, out object? value)
    {
        value = null;
        var type = Nullable.GetUnderlyingType(setting.PropertyType) ?? setting.PropertyType;
        try
        {
            switch (raw)
            {
                case null:
                    return false;
                case string text when type == typeof(string):
                    value = text;
                    return true;
                case string text when type == typeof(bool):
                    if (text.Trim() is "1" or "0")
                        value = text.Trim() == "1";
                    else if (bool.TryParse(text.Trim(), out var flag))
                        value = flag;
                    return value is not null;
                case int or long when type == typeof(bool):
                    value = System.Convert.ToInt64(raw, CultureInfo.InvariantCulture) != 0;
                    return true;
                case string text when type == typeof(int):
                    if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                        value = number;
                    return value is not null;
                case int or long when type == typeof(int):
                    value = checked((int)System.Convert.ToInt64(raw, CultureInfo.InvariantCulture));
                    return true;
                case string text when type.IsEnum:
                    // A name; a number written as text counts too
                    if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ordinal))
                        return TryConvert(setting, ordinal, out value);
                    if (Enum.TryParse(type, text.Trim(), ignoreCase: true, out var named) && Enum.IsDefined(type, named!))
                        value = named;
                    return value is not null;
                case int or long when type.IsEnum:
                    var numbered = Enum.ToObject(type, System.Convert.ToInt64(raw, CultureInfo.InvariantCulture));
                    if (Enum.IsDefined(type, numbered))
                        value = numbered;
                    return value is not null;
                case string[] lines when type == typeof(List<string>):
                    value = lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                    return true;
                case string json when !type.IsPrimitive:
                    value = JsonSerializer.Deserialize(json, setting.PropertyType, JsonOptions);
                    return value is not null;
                case JsonNode node:
                    value = node.Deserialize(setting.PropertyType, JsonOptions);
                    return value is not null || !setting.PropertyType.IsValueType;
                default:
                    return false;
            }
        }
        catch (Exception ex) when (ex is JsonException or OverflowException or FormatException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>
    /// The settings among <paramref name="values"/> (name to raw value), by their proper names.
    /// Names that aren't settings, and values that don't fit theirs, are left out and reported.
    /// </summary>
    public static Dictionary<string, object?> Convert(IEnumerable<KeyValuePair<string, object>> values, Action<string>? reject = null)
    {
        var settings = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, raw) in values)
        {
            if (Find(name) is not { } setting)
                reject?.Invoke($"{name} is not a Vox setting");
            else if (!TryConvert(setting, raw, out var value))
                reject?.Invoke($"{name}: {raw} is not a valid value");
            else
                settings[setting.Name] = value;
        }
        return settings;
    }

    /// <summary>Settings (proper name to value) as the JSON properties of a settings file.</summary>
    public static JsonObject ToJson(IReadOnlyDictionary<string, object?> settings)
    {
        var json = new JsonObject();
        foreach (var (name, value) in settings)
            json[name] = JsonSerializer.SerializeToNode(value, Find(name)!.PropertyType, JsonOptions);
        return json;
    }

    /// <summary>
    /// <paramref name="over"/>'s properties laid over <paramref name="under"/>'s (names matched in
    /// any case), as a new object; neither is changed.
    /// </summary>
    public static JsonObject Overlay(JsonObject? under, JsonObject over)
    {
        var result = under?.DeepClone() as JsonObject ?? new JsonObject();
        foreach (var (name, value) in over)
        {
            foreach (var existing in result.Select(p => p.Key).Where(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)).ToList())
                result.Remove(existing);
            result[name] = value?.DeepClone();
        }
        return result;
    }

    /// <summary>
    /// <paramref name="settings"/> with each of <paramref name="values"/> (proper name to value) put
    /// in. The same instance when it already had them all.
    /// </summary>
    public static VoxSettings With(VoxSettings settings, IReadOnlyDictionary<string, object?> values)
    {
        if (values.All(v => ValueEquals(Find(v.Key)!.GetValue(settings), v.Value)))
            return settings;
        var copy = settings with { };
        foreach (var (name, value) in values)
            Find(name)!.SetValue(copy, value);
        return copy;
    }

    private static bool ValueEquals(object? a, object? b) =>
        Equals(a, b)
        || (a is System.Collections.IEnumerable && b is System.Collections.IEnumerable
            && JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions));
}
