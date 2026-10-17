using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Vox.Core.AddOns;

/// <summary>
/// An add-on's manifest (addon.json, at the root of its package): who made it, its version, the
/// add-on API range it works with, the assembly Vox loads (or, for a label pack, its labels file),
/// and the SHA-256 of every file in the package, so the package's signature (addon.sig, over
/// addon.json's bytes) covers all of them.
/// </summary>
public sealed partial record AddOnManifest
{
    public const string FileName = "addon.json";

    /// <summary>The <see cref="Type"/> of a label pack: custom labels for web sites, no code (<see cref="Labels.LabelPack"/>).</summary>
    public const string LabelPackType = "labels";

    /// <summary>Lower case letters, digits, '.', '-' and '_' ("com.example.notepadplusplus"); its folder's name.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    /// <summary>The add-on's own version (2 to 4 numbers: "1.2.0").</summary>
    [JsonPropertyName("version")] public string Version { get; init; } = string.Empty;
    [JsonPropertyName("author")] public string Author { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; init; }
    /// <summary>The oldest add-on API it needs (major.minor).</summary>
    [JsonPropertyName("minApiVersion")] public string MinApiVersion { get; init; } = string.Empty;
    /// <summary>The newest add-on API it was tested with; null: <see cref="MinApiVersion"/>. Only its major version matters.</summary>
    [JsonPropertyName("maxApiVersion")] public string? MaxApiVersion { get; init; }
    /// <summary>What the add-on is: null (or "code") for an assembly, <see cref="LabelPackType"/> for a label pack.</summary>
    [JsonPropertyName("type")] public string? Type { get; init; }
    /// <summary>The assembly with the <c>Vox.Sdk.IAddOn</c> class, at the package's root (none for a label pack).</summary>
    [JsonPropertyName("entryAssembly")] public string EntryAssembly { get; init; } = string.Empty;
    /// <summary>Where its plug-ins and announcement filters come among the add-ons' (lowest first, ties by id); null: 0.</summary>
    [JsonPropertyName("order")] public int? Order { get; init; }
    /// <summary>A page about the add-on (https), if any.</summary>
    [JsonPropertyName("url")] public string? Url { get; init; }
    /// <summary>Every file of the package but the manifest, signature and key: its path ('/' separated) and SHA-256 (hex).</summary>
    [JsonPropertyName("files")] public Dictionary<string, string> Files { get; init; } = new(StringComparer.Ordinal);

    [JsonIgnore] public Version ParsedVersion => System.Version.Parse(Version);
    /// <summary>A label pack: its <see cref="Labels.LabelPack.FileName"/> holds custom labels; nothing is loaded as code.</summary>
    [JsonIgnore] public bool IsLabelPack => string.Equals(Type, LabelPackType, StringComparison.Ordinal);
    [JsonIgnore] public Version MinApi => System.Version.Parse(MinApiVersion);
    [JsonIgnore] public Version MaxApi => System.Version.Parse(MaxApiVersion ?? MinApiVersion);

    private static readonly JsonSerializerOptions ReadOptions = new() { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Reads and checks a manifest; throws <see cref="FormatException"/> naming what is wrong.</summary>
    public static AddOnManifest Parse(string json)
    {
        AddOnManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<AddOnManifest>(json, ReadOptions); }
        catch (JsonException ex) { throw new FormatException("The add-on's manifest isn't valid JSON.", ex); }
        if (manifest is null)
            throw new FormatException("The add-on's manifest is empty.");
        var problems = manifest.Problems();
        if (problems.Count > 0)
            throw new FormatException(string.Join(" ", problems));
        return manifest;
    }

    public string ToJson() => JsonSerializer.Serialize(this, WriteOptions);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Pattern();

    /// <summary>What is wrong with the manifest; empty when nothing is.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!IdPattern().IsMatch(Id ?? string.Empty) || Id!.Contains(".."))
            problems.Add($"The id '{Id}' must be 2 to 64 lower case letters, digits, '.', '-' or '_'.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 100)
            problems.Add("The name must be given (up to 100 characters).");
        if (!IsVersion(Version, 2, 4))
            problems.Add($"The version '{Version}' must be 2 to 4 numbers (1.2.0).");
        if (string.IsNullOrWhiteSpace(Author))
            problems.Add("The author must be given.");
        if (!IsVersion(MinApiVersion, 2, 2))
            problems.Add($"minApiVersion '{MinApiVersion}' must be major.minor (1.0).");
        else if (MaxApiVersion is not null && (!IsVersion(MaxApiVersion, 2, 2) || MaxApi < MinApi))
            problems.Add($"maxApiVersion '{MaxApiVersion}' must be major.minor, not below minApiVersion.");
        if (Order is < -1000 or > 1000)
            problems.Add("The order must be between -1000 and 1000.");
        if (Url is not null && (!Uri.TryCreate(Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps))
            problems.Add("The url must be an https address.");
        if (Files is null || Files.Count == 0)
            problems.Add("The manifest lists no files.");
        else
        {
            foreach (var (path, hash) in Files)
            {
                if (!IsSafePath(path) || IsReserved(path))
                    problems.Add($"The file path '{path}' isn't allowed.");
                if (!Sha256Pattern().IsMatch(hash ?? string.Empty))
                    problems.Add($"The file '{path}' has no SHA-256.");
            }
        }
        if (Type is not null && Type != "code" && !IsLabelPack)
            problems.Add($"The type '{Type}' isn't known (\"code\" or \"{LabelPackType}\").");
        else if (IsLabelPack)
        {
            if (Files is not null && !Files.ContainsKey(Labels.LabelPack.FileName))
                problems.Add($"A label pack must have {Labels.LabelPack.FileName}.");
        }
        else if (string.IsNullOrEmpty(EntryAssembly) || EntryAssembly.Contains('/') || EntryAssembly.Contains('\\')
            || !EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            problems.Add($"entryAssembly '{EntryAssembly}' must be a .dll at the package's root.");
        else if (Files is not null && !Files.ContainsKey(EntryAssembly))
            problems.Add($"entryAssembly '{EntryAssembly}' isn't one of the files.");
        return problems;
    }

    private static bool IsVersion(string? text, int minParts, int maxParts) =>
        text is not null && text.Split('.') is { } parts && parts.Length >= minParts && parts.Length <= maxParts
        && parts.All(p => p.Length is > 0 and <= 9 && p.All(char.IsAsciiDigit));

    /// <summary>A relative path inside the package: '/' separated, no "..", no drive or root, no odd characters.</summary>
    public static bool IsSafePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 200 || path.StartsWith('/') || path.Contains('\\') || path.Contains(':'))
            return false;
        foreach (var part in path.Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0)
                return false;
        }
        return true;
    }

    /// <summary>The package's own files, which the manifest doesn't list.</summary>
    public static bool IsReserved(string path) =>
        string.Equals(path, FileName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(path, AddOnPackage.SignatureFileName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(path, AddOnPackage.KeyFileName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Whether an add-on works with this Vox's add-on API.</summary>
public enum AddOnCompatibilityKind
{
    Compatible,
    /// <summary>It needs a newer API than this Vox has.</summary>
    NeedsNewerVox,
    /// <summary>It was made for an older major API this Vox no longer offers.</summary>
    MadeForOlderVox,
}

public static class AddOnCompatibility
{
    /// <summary>
    /// Compatible when this Vox's API is at least the add-on's minimum (major.minor) and its major
    /// version is no newer than the one the add-on was tested with: minor versions only add.
    /// </summary>
    public static AddOnCompatibilityKind Check(AddOnManifest manifest, Version api)
    {
        var current = new Version(api.Major, Math.Max(api.Minor, 0));
        if (current < manifest.MinApi)
            return AddOnCompatibilityKind.NeedsNewerVox;
        if (current.Major > manifest.MaxApi.Major)
            return AddOnCompatibilityKind.MadeForOlderVox;
        return AddOnCompatibilityKind.Compatible;
    }

    /// <summary>Why an add-on can't be used, as said to the user; null when it can.</summary>
    public static string? Reason(AddOnManifest manifest, Version api) => Check(manifest, api) switch
    {
        AddOnCompatibilityKind.NeedsNewerVox => Localization.VoxStrings.Format("AddOns.NeedsNewerVox", manifest.Name, manifest.MinApiVersion, Short(api)),
        AddOnCompatibilityKind.MadeForOlderVox => Localization.VoxStrings.Format("AddOns.MadeForOlderVox", manifest.Name, manifest.MaxApi.Major, Short(api)),
        _ => null,
    };

    private static string Short(Version v) => $"{v.Major}.{Math.Max(v.Minor, 0)}";
}
