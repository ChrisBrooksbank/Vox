using Microsoft.Extensions.Logging;

namespace Vox.Core.Speech;

/// <summary>
/// Applies the pronunciation dictionaries to every utterance, before symbols are processed (so
/// a rule can match "C#"): the user dictionary (dictionaries/user.json), then the current
/// voice's (dictionaries/voices/&lt;voice&gt;.json), then the built-in default dictionary. The
/// user's files are re-read when they change, so a new rule applies at once.
/// </summary>
public sealed class PronunciationRule : ITextRule, IDisposable
{
    public const string UserFileName = "user.json";
    public const string VoicesFolderName = "voices";

    private readonly PronunciationDictionary _default;
    private readonly string? _directory;
    private readonly Func<string?> _currentVoice;
    private readonly ILogger<PronunciationRule>? _logger;
    private readonly object _lock = new();
    private readonly Dictionary<string, PronunciationDictionary> _voices = new(StringComparer.OrdinalIgnoreCase);
    private PronunciationDictionary _user = PronunciationDictionary.Empty;
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _reloadTimer;

    /// <param name="directory">The user's dictionaries folder (null: none, e.g. on secure screens).</param>
    public PronunciationRule(PronunciationDictionary defaultDictionary, string? directory, Func<string?> currentVoice,
        ILogger<PronunciationRule>? logger = null)
    {
        _default = defaultDictionary;
        _directory = directory;
        _currentVoice = currentVoice;
        _logger = logger;
        Reload();
    }

    /// <summary>Watches the dictionaries folder and reloads shortly after a file changes.</summary>
    public void StartWatching()
    {
        if (_directory is null)
            return;
        try
        {
            Directory.CreateDirectory(_directory);
            _reloadTimer = new System.Threading.Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
            _watcher = new FileSystemWatcher(_directory, "*.json")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler changed = (_, _) => _reloadTimer?.Change(200, Timeout.Infinite);
            _watcher.Changed += changed;
            _watcher.Created += changed;
            _watcher.Deleted += changed;
            _watcher.Renamed += (_, _) => _reloadTimer?.Change(200, Timeout.Infinite);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not watch the pronunciation dictionaries");
        }
    }

    /// <summary>Re-reads the user's dictionaries (the voice dictionaries are read again when next needed).</summary>
    public void Reload()
    {
        var user = _directory is null ? PronunciationDictionary.Empty : Read(Path.Combine(_directory, UserFileName));
        lock (_lock)
        {
            _user = user;
            _voices.Clear();
        }
    }

    public string Apply(string text, Utterance utterance)
    {
        PronunciationDictionary user;
        lock (_lock) user = _user;
        text = user.Apply(text);
        text = VoiceDictionary(_currentVoice()).Apply(text);
        return _default.Apply(text);
    }

    private PronunciationDictionary VoiceDictionary(string? voice)
    {
        if (_directory is null || string.IsNullOrWhiteSpace(voice))
            return PronunciationDictionary.Empty;
        lock (_lock)
        {
            if (_voices.TryGetValue(voice, out var cached))
                return cached;
        }
        var dictionary = Read(VoiceFile(_directory, voice));
        lock (_lock) _voices[voice] = dictionary;
        return dictionary;
    }

    /// <summary>The file of <paramref name="voice"/>'s dictionary in <paramref name="directory"/>.</summary>
    public static string VoiceFile(string directory, string voice)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(voice.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return Path.Combine(directory, VoicesFolderName, name + ".json");
    }

    private PronunciationDictionary Read(string path)
    {
        try
        {
            if (!File.Exists(path))
                return PronunciationDictionary.Empty;
            var dictionary = PronunciationDictionary.FromJson(File.ReadAllText(path), out var warnings);
            foreach (var warning in warnings)
                _logger?.LogWarning("Pronunciation dictionary {File}: {Warning}", Path.GetFileName(path), warning);
            return dictionary;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _logger?.LogWarning(ex, "Could not read the pronunciation dictionary {File}", Path.GetFileName(path));
            return PronunciationDictionary.Empty;
        }
    }

    /// <summary>The built-in default dictionary (assets/speech/dictionary-default.json, embedded).</summary>
    public static PronunciationDictionary LoadBuiltInDefault()
    {
        using var stream = typeof(PronunciationRule).Assembly.GetManifestResourceStream("Vox.Core.speech.dictionary-default.json")
            ?? throw new InvalidOperationException("Built-in pronunciation dictionary is missing.");
        using var reader = new StreamReader(stream);
        return PronunciationDictionary.FromJson(reader.ReadToEnd(), out _);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _reloadTimer?.Dispose();
    }
}
