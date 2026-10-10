using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Vox.Core.Navigation;

/// <summary>
/// Header rows and columns the user set by hand for tables without header markup, kept per page
/// and table (<see cref="TableNavigator.TableKey"/>) and saved to a JSON file, so they still
/// apply when the page is visited again. Without a file (secure mode) they last the session.
/// Holds the most recently set <see cref="MaxEntries"/> tables. Call on the pipeline thread.
/// </summary>
public sealed class TableHeaderStore
{
    /// <summary>The 0-based row holding column headers and column holding row headers, each optional.</summary>
    public sealed record Headers(int? Row, int? Column)
    {
        public static readonly Headers None = new(null, null);
    }

    public const int MaxEntries = 500;

    private readonly string? _path;
    private readonly ILogger _logger;
    // Most recently set last
    private readonly List<(string Key, Headers Headers)> _entries = new();
    private bool _loaded;

    public TableHeaderStore(string? path = null, ILogger<TableHeaderStore>? logger = null)
    {
        _path = path;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>The default file, in the user's Vox folder.</summary>
    public static string DefaultPath => Path.Combine(
        Lifecycle.VoxPaths.UserData, "table-headers.json");

    /// <summary>The headers set for a table, or <see cref="Headers.None"/>.</summary>
    public Headers Get(string key)
    {
        Load();
        int i = IndexOf(key);
        return i >= 0 ? _entries[i].Headers : Headers.None;
    }

    /// <summary>Sets (or with null clears) the row holding a table's column headers.</summary>
    public void SetRow(string key, int? row) => Set(key, Get(key) with { Row = row });

    /// <summary>Sets (or with null clears) the column holding a table's row headers.</summary>
    public void SetColumn(string key, int? column) => Set(key, Get(key) with { Column = column });

    private void Set(string key, Headers headers)
    {
        int i = IndexOf(key);
        if (i >= 0)
            _entries.RemoveAt(i);
        if (headers.Row is not null || headers.Column is not null)
            _entries.Add((key, headers));
        while (_entries.Count > MaxEntries)
            _entries.RemoveAt(0);
        Save();
    }

    private int IndexOf(string key) => _entries.FindIndex(e => e.Key == key);

    private void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        if (_path is null || !File.Exists(_path))
            return;
        try
        {
            var file = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(_path));
            foreach (var entry in file?.Entries ?? [])
            {
                if (!string.IsNullOrEmpty(entry.Key) && (entry.Row is >= 0 || entry.Column is >= 0))
                    _entries.Add((entry.Key, new Headers(entry.Row is >= 0 ? entry.Row : null, entry.Column is >= 0 ? entry.Column : null)));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read table headers from {Path}", _path);
        }
    }

    private void Save()
    {
        if (_path is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var file = new StoreFile
            {
                Entries = _entries.Select(e => new StoreEntry { Key = e.Key, Row = e.Headers.Row, Column = e.Headers.Column }).ToList(),
            };
            var tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save table headers to {Path}", _path);
        }
    }

    private sealed class StoreFile
    {
        [JsonPropertyName("entries")]
        public List<StoreEntry> Entries { get; set; } = new();
    }

    private sealed class StoreEntry
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("row")]
        public int? Row { get; set; }

        [JsonPropertyName("column")]
        public int? Column { get; set; }
    }
}
