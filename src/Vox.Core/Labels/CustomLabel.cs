namespace Vox.Core.Labels;

/// <summary>
/// A name the user (or a label pack, or an automatic description) gave an element of a web site:
/// on <paramref name="Site"/> ("https://example.com"), optionally only under
/// <paramref name="PathPrefix"/> ("/mail"), the element with <paramref name="Signature"/>
/// (<see cref="ElementSignature"/>) is called <paramref name="Label"/>.
/// <paramref name="Described"/> marks a label that came from an automatic description.
/// </summary>
public sealed record CustomLabel(string Site, string Signature, string Label, string? PathPrefix = null, bool Described = false)
{
    /// <summary>Whether this label is for the same element and scope as <paramref name="other"/> (one replaces the other).</summary>
    public bool SameTarget(CustomLabel other) =>
        string.Equals(Site, other.Site, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Signature, other.Signature, StringComparison.Ordinal)
        && string.Equals(PathPrefix ?? string.Empty, other.PathPrefix ?? string.Empty, StringComparison.Ordinal);
}

/// <summary>
/// The labels that apply on one page: for each element signature, its label (a label scoped to a
/// longer path prefix wins over a shorter one). Immutable, so a buffer built with it can keep it.
/// </summary>
public sealed class SiteLabels
{
    private readonly IReadOnlyDictionary<string, string> _labels;

    public SiteLabels(string site, IReadOnlyDictionary<string, string> labels)
    {
        Site = site;
        _labels = labels;
    }

    /// <summary>The site ("https://example.com").</summary>
    public string Site { get; }

    public int Count => _labels.Count;

    /// <summary>The label of the element with <paramref name="signature"/>, or null.</summary>
    public string? Get(string signature) => _labels.TryGetValue(signature, out var label) ? label : null;
}

/// <summary>Where custom labels come from when a page is built (<see cref="CustomLabelStore"/>).</summary>
public interface ICustomLabels
{
    /// <summary>The labels for the page at <paramref name="address"/>, or null when there are none (or labels are off).</summary>
    SiteLabels? LabelsFor(string? address);
}

/// <summary>Sites as custom labels key them: scheme and host (and port), from a page's address.</summary>
public static class CustomLabelSites
{
    /// <summary>"https://example.com" for "https://example.com/a/b?c", or null for an address that is no web site.</summary>
    public static string? SiteOf(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;
        return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }

    /// <summary>The address's path ("/a/b"), or "/" when it has none or is no web address.</summary>
    public static string PathOf(string? address) =>
        Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri) ? uri.AbsolutePath : "/";

    /// <summary>Whether a label scoped to <paramref name="pathPrefix"/> applies on a page with <paramref name="path"/>.</summary>
    public static bool PathMatches(string? pathPrefix, string path) =>
        string.IsNullOrEmpty(pathPrefix) || path.StartsWith(pathPrefix, StringComparison.Ordinal);
}
