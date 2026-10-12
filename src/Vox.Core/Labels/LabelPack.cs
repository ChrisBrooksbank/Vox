using System.Text.Json;

namespace Vox.Core.Labels;

/// <summary>
/// A label pack: custom labels to share, in the labels file's format
/// (<c>{ "sites": { "https://example.com": [ { "signature": ..., "label": ... } ] } }</c>).
/// Exported and imported as a <see cref="Extension"/> file from the custom labels dialog, or
/// published as a label-pack add-on (<see cref="AddOns.AddOnManifest.LabelPackType"/>, its labels
/// in <see cref="FileName"/>), installed from the add-on store like any other package.
/// </summary>
public static class LabelPack
{
    /// <summary>A label pack file's extension.</summary>
    public const string Extension = ".voxlabels";

    /// <summary>The labels file in a label-pack add-on.</summary>
    public const string FileName = "labels.json";

    /// <summary>A label pack is small; anything bigger isn't one.</summary>
    public const long MaxBytes = 4 * 1024 * 1024;

    /// <summary>The JSON of a pack holding <paramref name="labels"/>.</summary>
    public static string ToJson(IEnumerable<CustomLabel> labels) => CustomLabelStore.ToJson(labels);

    /// <summary>The labels of a pack's JSON; throws <see cref="FormatException"/> when it is none.</summary>
    public static IReadOnlyList<CustomLabel> Parse(string json)
    {
        try
        {
            return CustomLabelStore.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The label pack isn't valid JSON.", ex);
        }
    }

    /// <summary>Reads the pack at <paramref name="path"/>; throws <see cref="FormatException"/> or <see cref="IOException"/>.</summary>
    public static IReadOnlyList<CustomLabel> Read(string path)
    {
        if (new FileInfo(path).Length > MaxBytes)
            throw new FormatException("The file is too big to be a label pack.");
        return Parse(File.ReadAllText(path));
    }

    /// <summary>Writes <paramref name="labels"/> as a pack at <paramref name="path"/>.</summary>
    public static void Write(string path, IEnumerable<CustomLabel> labels)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, ToJson(labels));
    }

    /// <summary>The sites of <paramref name="labels"/>, by name.</summary>
    public static IReadOnlyList<string> SitesOf(IEnumerable<CustomLabel> labels) =>
        labels.Select(l => l.Site).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
}
