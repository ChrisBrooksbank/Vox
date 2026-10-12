using Vox.Core.Localization;

namespace Vox.Core.Labels;

/// <summary>
/// What the custom labels dialog edits: a working copy of the labels, the current site's first,
/// renamed and deleted there and saved only when the dialog closes with OK.
/// </summary>
public sealed class CustomLabelsEditor
{
    private readonly List<CustomLabel> _labels;

    public CustomLabelsEditor(IEnumerable<CustomLabel> labels, string? currentSite = null)
    {
        CurrentSite = currentSite;
        _labels = labels
            .OrderBy(l => string.Equals(l.Site, currentSite, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(l => l.Site, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>The site of the page being browsed, whose labels come first; null when none.</summary>
    public string? CurrentSite { get; }

    public IReadOnlyList<CustomLabel> Labels => _labels;

    /// <summary>Whether anything was renamed or deleted.</summary>
    public bool Changed { get; private set; }

    /// <summary>How the list shows a label: "Search, example.com" (with its path and "described" when so).</summary>
    public string Describe(int index)
    {
        var label = _labels[index];
        var site = label.Site.Contains("://", StringComparison.Ordinal) ? label.Site[(label.Site.IndexOf("://", StringComparison.Ordinal) + 3)..] : label.Site;
        var text = VoxStrings.Format("Labels.Entry", label.Label, site + (label.PathPrefix ?? string.Empty));
        return label.Described ? VoxStrings.Format("Labels.EntryDescribed", text) : text;
    }

    /// <summary>Gives the label at <paramref name="index"/> a new text (no longer "described"); empty text changes nothing.</summary>
    public bool Rename(int index, string text)
    {
        if (index < 0 || index >= _labels.Count || string.IsNullOrWhiteSpace(text))
            return false;
        var renamed = _labels[index] with { Label = text.Trim(), Described = false };
        if (renamed == _labels[index])
            return false;
        _labels[index] = renamed;
        Changed = true;
        return true;
    }

    /// <summary>
    /// Adds <paramref name="labels"/> (an imported pack), each replacing the label for the same
    /// element; how many there were.
    /// </summary>
    public int Merge(IReadOnlyCollection<CustomLabel> labels)
    {
        if (labels.Count == 0)
            return 0;
        _labels.RemoveAll(l => labels.Any(n => n.SameTarget(l)));
        _labels.AddRange(labels);
        Changed = true;
        return labels.Count;
    }

    /// <summary>The site whose labels an export takes: the selected label's, else the current site, else null.</summary>
    public string? ExportSite(int index) =>
        index >= 0 && index < _labels.Count ? _labels[index].Site
        : CurrentSite is not null && _labels.Any(l => SameSite(l, CurrentSite)) ? CurrentSite
        : null;

    /// <summary>The labels of <paramref name="site"/>, as they are now in the dialog.</summary>
    public IReadOnlyList<CustomLabel> LabelsOf(string site) => _labels.Where(l => SameSite(l, site)).ToList();

    private static bool SameSite(CustomLabel label, string site) => string.Equals(label.Site, site, StringComparison.OrdinalIgnoreCase);

    public bool Delete(int index)
    {
        if (index < 0 || index >= _labels.Count)
            return false;
        _labels.RemoveAt(index);
        Changed = true;
        return true;
    }
}
