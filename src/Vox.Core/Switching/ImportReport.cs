namespace Vox.Core.Switching;

/// <summary>The screen reader settings are brought over from.</summary>
public enum ImportSource
{
    Nvda,
    Jaws
}

/// <summary>Why something in the other screen reader's settings was not brought over.</summary>
public enum ImportSkipReason
{
    /// <summary>Vox has nothing like it (an unknown synthesizer, a braille driver Vox lacks, a script with no command).</summary>
    NoCounterpart,
    /// <summary>Vox has it, but it isn't installed on this computer (eSpeak NG, a voice, a braille table).</summary>
    NotInstalled,
    /// <summary>The value could not be understood.</summary>
    InvalidValue,
    /// <summary>The line could not be read at all.</summary>
    Malformed,
    /// <summary>Vox can do it only one way and kept its own (one screen reader key, a regular expression Vox's rule can't take).</summary>
    Unsupported,
    /// <summary>The source file could not be read.</summary>
    Unreadable
}

/// <summary>
/// Something brought over: where it came from in the source ("speech/oneCore/rate", "line 12"),
/// the Vox setting (or file) it went to, its new value and the value it replaced.
/// </summary>
public sealed record ImportedItem(string Source, string Target, string? Value, string? PreviousValue)
{
    /// <summary>The import changed Vox's value (it may already have been the same).</summary>
    public bool Changed => !string.Equals(Value, PreviousValue, StringComparison.Ordinal);
}

/// <summary>Something not brought over: where it was, its value, and why.</summary>
public sealed record SkippedItem(string Source, string? Value, ImportSkipReason Reason, string? Detail = null);

/// <summary>
/// What a settings import brought over and what it skipped, and why — shown to the user to review
/// (nothing is changed without saying so). Shared by every importer (NVDA settings, gestures and
/// dictionaries; JAWS), which add to one report.
/// </summary>
public sealed class ImportReport
{
    private readonly List<ImportedItem> _imported = [];
    private readonly List<SkippedItem> _skipped = [];
    private readonly List<string> _files = [];

    public ImportReport(ImportSource source) => Source = source;

    public ImportSource Source { get; }

    /// <summary>The files that were read, in order.</summary>
    public IReadOnlyList<string> Files => _files;

    public IReadOnlyList<ImportedItem> Imported => _imported;

    public IReadOnlyList<SkippedItem> Skipped => _skipped;

    /// <summary>The import changed at least one of Vox's settings or files.</summary>
    public bool HasChanges => _imported.Any(i => i.Changed);

    public void AddFile(string path) => _files.Add(path);

    public void AddImported(string source, string target, object? value, object? previousValue) =>
        _imported.Add(new ImportedItem(source, target, Format(value), Format(previousValue)));

    public void AddSkipped(string source, string? value, ImportSkipReason reason, string? detail = null) =>
        _skipped.Add(new SkippedItem(source, value, reason, detail));

    /// <summary>Reports a source's malformed lines as skipped.</summary>
    public void AddMalformed(string file, IEnumerable<MalformedLine> lines)
    {
        foreach (var line in lines)
            AddSkipped($"{Path.GetFileName(file)}:{line.Number}", line.Text.Trim(), ImportSkipReason.Malformed);
    }

    /// <summary>A value as the report shows it: invariant, enums by name, null for nothing.</summary>
    public static string? Format(object? value) => value switch
    {
        null => null,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
