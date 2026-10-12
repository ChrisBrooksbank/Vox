using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Vox.Core.Localization;

namespace Vox.Core.Configuration;

/// <summary>
/// Settings kept for a web site: the mode its pages start in, verbosity, screen layout and
/// whether cookie banners are rejected automatically. A site's settings are a sparse layer, like
/// a profile's, laid over the application's profile while one of its pages is browsed
/// (<see cref="ProfileManager.SetSite"/>).
/// </summary>
public static class SiteSettings
{
    /// <summary>The settings a site can have.</summary>
    public static readonly IReadOnlyList<string> Keys =
    [
        nameof(VoxSettings.WebStartMode), nameof(VoxSettings.VerbosityLevel),
        nameof(VoxSettings.ScreenLayout), nameof(VoxSettings.AutoDismissOverlays),
    ];

    /// <summary>The site settings among <paramref name="settings"/> (the rest left out).</summary>
    public static JsonObject Only(JsonObject settings)
    {
        var only = new JsonObject();
        foreach (var key in Keys)
        {
            if (settings[key] is { } value)
                only[key] = value.DeepClone();
        }
        return only;
    }
}

/// <summary>
/// The sites' settings, in <c>%APPDATA%\Vox\sites.json</c>:
/// <c>{ "https://example.com": { "VerbosityLevel": "Beginner" } }</c>. Thread-safe. Without a
/// file path (secure screens) there are none and none can be kept.
/// </summary>
public sealed class SiteSettingsStore
{
    public const string FileName = "sites.json";

    private readonly string? _path;
    private readonly ILogger? _logger;
    private readonly object _lock = new();
    private Dictionary<string, JsonObject> _sites = new(StringComparer.OrdinalIgnoreCase);

    public SiteSettingsStore(string? path, ILogger<SiteSettingsStore>? logger = null)
    {
        _path = path;
        _logger = logger;
        if (path is not null && File.Exists(path))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) is JsonObject file)
                {
                    foreach (var (site, settings) in file)
                    {
                        if (Labels.CustomLabelSites.SiteOf(site) is { } key && settings is JsonObject values && SiteSettings.Only(values) is { Count: > 0 } only)
                            _sites[key] = only;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                logger?.LogWarning(ex, "Could not read the site settings in {Path}", path);
            }
        }
    }

    public bool IsAvailable => _path is not null;

    /// <summary>The sites with settings.</summary>
    public IReadOnlyList<string> Sites
    {
        get { lock (_lock) return _sites.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList(); }
    }

    /// <summary>A copy of the site's settings (empty when it has none).</summary>
    public JsonObject For(string? site)
    {
        lock (_lock)
            return site is not null && _sites.TryGetValue(site, out var settings) ? (JsonObject)settings.DeepClone() : new JsonObject();
    }

    /// <summary>Keeps <paramref name="settings"/> (only site settings) for the site; none removes it. False when it could not be saved.</summary>
    public bool Set(string site, JsonObject settings)
    {
        if (_path is null)
            return false;
        var only = SiteSettings.Only(settings);
        lock (_lock)
        {
            var sites = new Dictionary<string, JsonObject>(_sites, StringComparer.OrdinalIgnoreCase);
            if (only.Count == 0)
                sites.Remove(site);
            else
                sites[site] = only;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                var file = new JsonObject();
                foreach (var (key, value) in sites.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
                    file[key] = value.DeepClone();
                var temp = _path + ".tmp";
                File.WriteAllText(temp, file.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Could not save the site settings");
                return false;
            }
            _sites = sites;
        }
        return true;
    }
}

/// <summary>One setting of the site settings dialog: its choices, the first meaning "as usual" (not set for the site).</summary>
public sealed record SiteSettingField(string Key, string Label, IReadOnlyList<SettingChoice> Choices)
{
    /// <summary>The choice selected.</summary>
    public int Selected { get; set; }
}

/// <summary>
/// What the site settings dialog edits: each site setting set to a value or left as usual (the
/// "as usual" choice names the current usual value). Its result is the site's sparse settings.
/// </summary>
public sealed class SiteSettingsEditor
{
    public SiteSettingsEditor(string site, JsonObject current, VoxSettings usual)
    {
        Site = site;
        Fields =
        [
            Field(nameof(VoxSettings.WebStartMode), VoxStrings.Get("Setting.WebStartMode"), current,
                Choices(usual.WebStartMode), SettingsPages.ChoiceName(usual.WebStartMode)),
            Field(nameof(VoxSettings.VerbosityLevel), VoxStrings.Get("Setting.VerbosityLevel"), current,
                Choices(usual.VerbosityLevel), SettingsPages.ChoiceName(usual.VerbosityLevel)),
            Field(nameof(VoxSettings.ScreenLayout), VoxStrings.Get("Setting.ScreenLayout"), current,
                OnOff(), OnOffName(usual.ScreenLayout)),
            Field(nameof(VoxSettings.AutoDismissOverlays), VoxStrings.Get("Setting.AutoDismissOverlays"), current,
                OnOff(), OnOffName(usual.AutoDismissOverlays)),
        ];
    }

    /// <summary>The site ("https://example.com").</summary>
    public string Site { get; }

    /// <summary>The site as shown: its host ("example.com").</summary>
    public string SiteName => Uri.TryCreate(Site, UriKind.Absolute, out var uri) ? uri.Authority : Site;

    public IReadOnlyList<SiteSettingField> Fields { get; }

    /// <summary>The site's settings as chosen: only those not left as usual.</summary>
    public JsonObject ToJson()
    {
        var settings = new JsonObject();
        foreach (var field in Fields)
        {
            if (field.Selected > 0 && field.Choices[field.Selected].Value is JsonNode value)
                settings[field.Key] = value.DeepClone();
        }
        return settings;
    }

    private static SiteSettingField Field(string key, string label, JsonObject current, List<SettingChoice> values, string usual)
    {
        var choices = new List<SettingChoice> { new(VoxStrings.Format("SiteSettings.Usual", usual), null) };
        choices.AddRange(values);
        int selected = current[key] is { } set ? choices.FindIndex(c => c.Value is JsonNode node && JsonNode.DeepEquals(node, set)) : 0;
        return new SiteSettingField(key, label.Replace("&", string.Empty), choices) { Selected = Math.Max(0, selected) };
    }

    private static List<SettingChoice> Choices<T>(T _) where T : struct, Enum =>
        Enum.GetValues<T>().Select(v => new SettingChoice(SettingsPages.ChoiceName(v), JsonValue.Create(v.ToString()))).ToList();

    private static List<SettingChoice> OnOff() =>
        [new(VoxStrings.Get("SiteSettings.On"), JsonValue.Create(true)), new(VoxStrings.Get("SiteSettings.Off"), JsonValue.Create(false))];

    private static string OnOffName(bool on) => VoxStrings.Get(on ? "SiteSettings.On" : "SiteSettings.Off");
}
