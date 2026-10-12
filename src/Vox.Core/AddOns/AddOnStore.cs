using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Vox.Core.Lifecycle;

namespace Vox.Core.AddOns;

/// <summary>An add-on in the catalogue: what it is, the API range it works with, and where its package is with its SHA-256.</summary>
public sealed record CatalogueEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("minApiVersion")] string MinApiVersion,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("maxApiVersion")] string? MaxApiVersion = null,
    [property: JsonPropertyName("type")] string? Type = null)
{
    /// <summary>As a manifest, for the compatibility check.</summary>
    public AddOnManifest AsManifest() => new()
    {
        Id = Id, Name = Name, Version = Version, Author = Author, Description = Description,
        MinApiVersion = MinApiVersion, MaxApiVersion = MaxApiVersion, Type = Type,
    };

    /// <summary>A label pack (<see cref="AddOnManifest.LabelPackType"/>) rather than code.</summary>
    public bool IsLabelPack => AsManifest().IsLabelPack;
}

/// <summary>A catalogue entry with what is installed of it.</summary>
/// <param name="Installed">The version installed, or null.</param>
/// <param name="Incompatible">Why it can't be used with this Vox, or null.</param>
public sealed record StoreItem(CatalogueEntry Entry, string? Installed, string? Incompatible)
{
    /// <summary>Installed, and the catalogue has a newer version that works with this Vox.</summary>
    public bool UpdateAvailable => Installed is not null && Incompatible is null
        && System.Version.TryParse(Installed, out var installed) && System.Version.Parse(Entry.Version) > installed;
}

/// <summary>
/// Vox's add-on catalogue (catalogue.json, with catalogue.json.sig beside it: base64 RSA-SHA256 of
/// its bytes by a trusted key, <see cref="AddOnTrustStore"/>). Packages are trusted because their
/// SHA-256 is the signed catalogue's (<see cref="AddOnManager.CheckPackage"/>).
/// </summary>
public static class AddOnCatalogue
{
    private sealed record CatalogueFile([property: JsonPropertyName("addons")] List<CatalogueEntry>? AddOns);

    /// <summary>
    /// The entries of a catalogue read from <paramref name="source"/>; package addresses may be
    /// relative to it. Remote catalogues and packages must be https; a catalogue on this computer
    /// (file:) may list packages beside it. Throws <see cref="FormatException"/>.
    /// </summary>
    public static IReadOnlyList<CatalogueEntry> Parse(string json, Uri source)
    {
        CatalogueFile? file;
        try { file = JsonSerializer.Deserialize<CatalogueFile>(json); }
        catch (JsonException ex) { throw new FormatException("The add-on catalogue isn't valid JSON.", ex); }
        var entries = file?.AddOns ?? throw new FormatException("The add-on catalogue has no add-ons list.");
        var result = new List<CatalogueEntry>();
        foreach (var entry in entries)
        {
            if (entry is null || !Uri.TryCreate(source, entry.Url, out var url))
                throw new FormatException("A catalogue entry has no package address.");
            // The package's own files aren't known yet: stand-ins check the rest
            var standIn = entry.IsLabelPack ? Labels.LabelPack.FileName : "x.dll";
            var problems = entry.AsManifest() with
            {
                EntryAssembly = entry.IsLabelPack ? string.Empty : standIn,
                Files = new Dictionary<string, string> { [standIn] = entry.Sha256 ?? string.Empty },
            };
            var wrong = problems.Problems();
            if (wrong.Count > 0)
                throw new FormatException($"Catalogue entry {entry.Id}: {string.Join(" ", wrong)}");
            bool allowed = url.Scheme == Uri.UriSchemeHttps || (url.IsFile && source.IsFile);
            if (!allowed)
                throw new FormatException($"Catalogue entry {entry.Id} isn't served over HTTPS.");
            result.Add(entry with { Url = url.AbsoluteUri, Sha256 = entry.Sha256!.ToLowerInvariant() });
        }
        return result;
    }

    /// <summary>The newest compatible entry per add-on (a catalogue may list several versions), by name.</summary>
    public static IReadOnlyList<StoreItem> Browse(IEnumerable<CatalogueEntry> entries, IEnumerable<AddOnInfo> installed, Version api)
    {
        var versions = installed.ToDictionary(a => a.Manifest.Id, a => a.Manifest.Version, StringComparer.Ordinal);
        return entries
            .GroupBy(e => e.Id, StringComparer.Ordinal)
            .Select(g =>
            {
                var ordered = g.OrderByDescending(e => System.Version.Parse(e.Version)).ToList();
                var best = ordered.FirstOrDefault(e => AddOnCompatibility.Check(e.AsManifest(), api) == AddOnCompatibilityKind.Compatible) ?? ordered[0];
                return new StoreItem(best, versions.GetValueOrDefault(best.Id), AddOnCompatibility.Reason(best.AsManifest(), api));
            })
            .OrderBy(i => i.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>The items matching every word of <paramref name="query"/> in their name, description, author or id.</summary>
    public static IReadOnlyList<StoreItem> Search(IEnumerable<StoreItem> items, string? query)
    {
        var words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return items.Where(i => words.All(w =>
            i.Entry.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase)
            || (i.Entry.Description?.Contains(w, StringComparison.CurrentCultureIgnoreCase) ?? false)
            || i.Entry.Author.Contains(w, StringComparison.CurrentCultureIgnoreCase)
            || i.Entry.Id.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
    }
}

/// <summary>Why the store can't be used, or that it can.</summary>
public enum StoreAvailability
{
    Available,
    /// <summary>No trusted key to check the catalogue with (assets/config/addon-key.pem or the user's keys).</summary>
    NoKey,
    /// <summary>The network is off (DisableNetwork policy) and the catalogue isn't on this computer.</summary>
    NetworkOff,
    /// <summary>Add-ons are off here (secure screens, DisableAddOns policy).</summary>
    AddOnsOff,
}

/// <summary>
/// The add-on store client: fetches and checks the signed catalogue, and downloads a package into
/// a new private folder for <see cref="AddOnManager.CheckPackage"/> to check against the
/// catalogue's checksum. All network use goes through the HttpClient it is given (NetworkGate's).
/// </summary>
public sealed class AddOnStore(HttpClient http, Func<Uri> catalogueUrl, AddOnTrustStore trust, RunPolicy policy, ILogger<AddOnStore> logger)
{
    /// <summary>Vox's catalogue (catalogue.json.sig beside it).</summary>
    public static readonly Uri DefaultCatalogueUrl = new("https://raw.githubusercontent.com/ChrisBrooksbank/Vox/master/addons/catalogue.json");

    public StoreAvailability Availability =>
        !policy.AllowAddOns ? StoreAvailability.AddOnsOff
        : !trust.HasKeys ? StoreAvailability.NoKey
        : !MayReach(catalogueUrl()) ? StoreAvailability.NetworkOff
        : StoreAvailability.Available;

    // A file on this computer is always readable; one on another (\\server\share) is the network
    private bool MayReach(Uri url) =>
        url.IsFile ? Enterprise.NetworkGate.MayRead(policy, url) : Enterprise.NetworkGate.MayConnect(policy, url.Host);

    /// <summary>The catalogue's entries; throws <see cref="AddOnStoreException"/> when it can't be fetched or its signature is wrong.</summary>
    public async Task<IReadOnlyList<CatalogueEntry>> FetchAsync(CancellationToken cancellationToken = default)
    {
        if (Availability != StoreAvailability.Available)
            throw new AddOnStoreException(Availability.ToString());
        var url = catalogueUrl();
        byte[] catalogue;
        string signature;
        try
        {
            catalogue = await ReadAsync(url, cancellationToken).ConfigureAwait(false);
            signature = System.Text.Encoding.ASCII.GetString(await ReadAsync(new Uri(url.AbsoluteUri + ".sig"), cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            logger.LogInformation(ex, "The add-on catalogue couldn't be fetched");
            throw new AddOnStoreException("The add-on catalogue couldn't be fetched.", ex);
        }
        if (!trust.IsSignedByTrustedKey(catalogue, signature))
        {
            logger.LogWarning("The add-on catalogue's signature doesn't match a trusted key; ignored");
            throw new AddOnStoreException("The add-on catalogue's signature doesn't check out.");
        }
        try
        {
            return AddOnCatalogue.Parse(System.Text.Encoding.UTF8.GetString(catalogue), url);
        }
        catch (FormatException ex)
        {
            throw new AddOnStoreException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Downloads <paramref name="entry"/>'s package into a new randomly named folder and returns
    /// its path; the caller checks it (against <see cref="CatalogueEntry.Sha256"/>) and deletes the folder.
    /// </summary>
    public async Task<string> DownloadAsync(CatalogueEntry entry, CancellationToken cancellationToken = default)
    {
        var folder = Directory.CreateTempSubdirectory("Vox-addon-");
        var path = Path.Combine(folder.FullName, entry.Id + AddOnPackage.Extension);
        try
        {
            var url = new Uri(entry.Url);
            if (url.IsFile)
            {
                if (!MayReach(url))
                    throw new Enterprise.NetworkDisabledException(url.Host);
                File.Copy(url.LocalPath, path);
            }
            else
            {
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > AddOnPackage.MaxTotalBytes)
                    throw new AddOnStoreException("The add-on is too large.");
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > AddOnPackage.MaxTotalBytes)
                        throw new AddOnStoreException("The add-on is too large.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
            return path;
        }
        catch (Exception ex)
        {
            DeleteDownload(path);
            if (ex is AddOnStoreException or OperationCanceledException)
                throw;
            logger.LogInformation(ex, "Add-on {AddOn} couldn't be downloaded", entry.Id);
            throw new AddOnStoreException("The add-on couldn't be downloaded.", ex);
        }
    }

    /// <summary>Deletes a downloaded package and its folder.</summary>
    public static void DeleteDownload(string path)
    {
        try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    private async Task<byte[]> ReadAsync(Uri url, CancellationToken cancellationToken)
    {
        if (!url.IsFile)
            return await http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        if (!MayReach(url))
            throw new Enterprise.NetworkDisabledException(url.Host);
        return await File.ReadAllBytesAsync(url.LocalPath, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>The store couldn't do what was asked.</summary>
public sealed class AddOnStoreException(string message, Exception? inner = null) : Exception(message, inner);
