using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Vox.Core.Labels;

/// <summary>
/// The user's custom labels, kept in <c>%APPDATA%\Vox\labels.json</c> by site, and those of the
/// label-pack add-ons installed (<see cref="SetPack"/>), under the user's own. Safe from any
/// thread: readers get an immutable snapshot. Without a file path (secure screens) there are no
/// labels and none can be added.
/// </summary>
public sealed class CustomLabelStore : ICustomLabels
{
    /// <summary>The file name under the user's Vox folder.</summary>
    public const string FileName = "labels.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string? _path;
    private readonly ILogger? _logger;
    private readonly object _lock = new();
    private IReadOnlyList<CustomLabel> _labels = [];
    private IReadOnlyDictionary<string, IReadOnlyList<CustomLabel>> _packs = new Dictionary<string, IReadOnlyList<CustomLabel>>();

    public CustomLabelStore(string? path, ILogger<CustomLabelStore>? logger = null)
    {
        _path = path;
        _logger = logger;
        if (path is not null)
            _labels = Read(path, logger);
    }

    /// <summary>Whether labels can be kept here (not on secure screens).</summary>
    public bool IsAvailable => _path is not null;

    /// <summary>Raised after the labels change (on the thread that changed them).</summary>
    public event EventHandler? Changed;

    /// <summary>The labels label-pack add-ons give, by add-on id.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<CustomLabel>> Packs
    {
        get { lock (_lock) return _packs; }
    }

    /// <summary>Uses the labels of the label-pack add-on <paramref name="id"/> (replacing what it gave before); not kept in the user's file.</summary>
    public void SetPack(string id, IReadOnlyList<CustomLabel> labels)
    {
        if (_path is null)
            return;
        lock (_lock)
            _packs = new Dictionary<string, IReadOnlyList<CustomLabel>>(_packs, StringComparer.Ordinal) { [id] = labels };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops using the labels of the label-pack add-on <paramref name="id"/>.</summary>
    public void RemovePack(string id)
    {
        lock (_lock)
        {
            if (!_packs.ContainsKey(id))
                return;
            var packs = new Dictionary<string, IReadOnlyList<CustomLabel>>(_packs, StringComparer.Ordinal);
            packs.Remove(id);
            _packs = packs;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The user's own labels, by site.</summary>
    public IReadOnlyList<CustomLabel> All
    {
        get { lock (_lock) return _labels; }
    }

    public SiteLabels? LabelsFor(string? address)
    {
        if (CustomLabelSites.SiteOf(address) is not { } site)
            return null;
        var path = CustomLabelSites.PathOf(address);
        var labels = new Dictionary<string, (string Label, int PrefixLength)>(StringComparer.Ordinal);
        IEnumerable<CustomLabel> packs, own;
        lock (_lock)
        {
            packs = _packs.OrderBy(p => p.Key, StringComparer.Ordinal).SelectMany(p => p.Value);
            own = _labels;
        }
        // The packs' first: the user's own labels win over theirs for the same element
        foreach (var label in packs.Concat(own))
        {
            if (!string.Equals(label.Site, site, StringComparison.OrdinalIgnoreCase) || !CustomLabelSites.PathMatches(label.PathPrefix, path))
                continue;
            int prefix = label.PathPrefix?.Length ?? 0;
            if (!labels.TryGetValue(label.Signature, out var found) || prefix >= found.PrefixLength)
                labels[label.Signature] = (label.Label, prefix);
        }
        return labels.Count == 0 ? null : new SiteLabels(site, labels.ToDictionary(l => l.Key, l => l.Value.Label, StringComparer.Ordinal));
    }

    /// <summary>The label for the element with <paramref name="signature"/> on the site, whole-site scope, or null.</summary>
    public CustomLabel? Find(string site, string signature) =>
        All.FirstOrDefault(l => string.Equals(l.Site, site, StringComparison.OrdinalIgnoreCase)
            && l.Signature == signature && string.IsNullOrEmpty(l.PathPrefix));

    /// <summary>Adds <paramref name="label"/>, replacing one for the same element and scope; false when it could not be saved.</summary>
    public bool Set(CustomLabel label) => SetAll([label]);

    /// <summary>Adds each of <paramref name="labels"/>, replacing those for the same element and scope.</summary>
    public bool SetAll(IReadOnlyCollection<CustomLabel> labels) =>
        Update(current => [.. current.Where(l => !labels.Any(n => n.SameTarget(l))), .. labels]);

    /// <summary>Removes the label for the same element and scope as <paramref name="label"/>.</summary>
    public bool Remove(CustomLabel label) => Update(current => [.. current.Where(l => !l.SameTarget(label))]);

    /// <summary>Replaces every label (the labels dialog's result).</summary>
    public bool ReplaceAll(IReadOnlyList<CustomLabel> labels) => Update(_ => [.. labels]);

    private bool Update(Func<IReadOnlyList<CustomLabel>, List<CustomLabel>> change)
    {
        if (_path is null)
            return false;
        lock (_lock)
        {
            var updated = change(_labels).Where(l => !string.IsNullOrWhiteSpace(l.Label)).ToList();
            try
            {
                Write(_path, updated);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Could not save the custom labels");
                return false;
            }
            _labels = updated;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private static IReadOnlyList<CustomLabel> Read(string path, ILogger? logger)
    {
        if (!File.Exists(path))
            return [];
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger?.LogWarning(ex, "Could not read the custom labels in {Path}", path);
            return [];
        }
    }

    /// <summary>The labels in a labels file's JSON (sites, each with its labels); entries missing a signature or label are left out.</summary>
    public static IReadOnlyList<CustomLabel> Parse(string json)
    {
        var file = JsonSerializer.Deserialize<LabelsFile>(json, JsonOptions);
        var labels = new List<CustomLabel>();
        foreach (var (site, entries) in file?.Sites ?? [])
        {
            if (CustomLabelSites.SiteOf(site) is not { } key)
                continue;
            foreach (var entry in entries ?? [])
            {
                if (entry is { Signature: { Length: > 0 } signature, Label: { } text } && !string.IsNullOrWhiteSpace(text))
                    labels.Add(new CustomLabel(key, signature, text.Trim(), string.IsNullOrEmpty(entry.PathPrefix) ? null : entry.PathPrefix, entry.Described));
            }
        }
        return labels;
    }

    /// <summary>The JSON of a labels file holding <paramref name="labels"/>.</summary>
    public static string ToJson(IEnumerable<CustomLabel> labels)
    {
        var file = new LabelsFile
        {
            Sites = labels.GroupBy(l => l.Site, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (List<LabelEntry>?)g.Select(l => new LabelEntry
                {
                    Signature = l.Signature,
                    Label = l.Label,
                    PathPrefix = l.PathPrefix,
                    Described = l.Described,
                }).ToList()),
        };
        return JsonSerializer.Serialize(file, JsonOptions);
    }

    private static void Write(string path, IEnumerable<CustomLabel> labels)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, ToJson(labels));
        File.Move(temp, path, overwrite: true);
    }

    private sealed class LabelsFile
    {
        public Dictionary<string, List<LabelEntry>?>? Sites { get; set; }
    }

    private sealed class LabelEntry
    {
        public string? Signature { get; set; }
        public string? Label { get; set; }
        public string? PathPrefix { get; set; }
        public bool Described { get; set; }
    }
}
