using Vox.Core.Input;
using Vox.Core.Speech;
using Vox.Core.Switching;
using Xunit;

namespace Vox.Core.Tests.Switching;

public sealed class ImportFilesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "vox-import-" + Guid.NewGuid().ToString("N"));

    public ImportFilesTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static readonly IReadOnlyList<KeyBinding> Layout =
    [
        new(KeyModifiers.Insert, 40, "Any", "SayAll"),
        new(KeyModifiers.None, 'H', "Browse", "NextHeading"),
        new(KeyModifiers.None, 'J', "Browse", "NextUnvisitedHeading"),
    ];

    [Fact]
    public void KeymapMerge_BindsInTheCommandsModes_AndSaysWhatTheKeyDidBefore()
    {
        var report = new ImportReport(ImportSource.Nvda);
        var merged = KeymapImport.Merge(Layout, [],
        [
            new ImportedKey("a", NavigationCommand.SayAll, KeyModifiers.Insert, 'A', "Browse"),
            new ImportedKey("j", NavigationCommand.NextHeading, KeyModifiers.None, 'J', "Any"),
            new ImportedKey("t", NavigationCommand.SayTime, KeyModifiers.Insert, 'T', "Any"),
        ], report);

        Assert.Contains(new KeyBinding(KeyModifiers.Insert, 'A', "Any", "SayAll"), merged);
        Assert.Contains(new KeyBinding(KeyModifiers.None, 'J', "Browse", "NextHeading"), merged);
        Assert.Contains(new KeyBinding(KeyModifiers.Insert, 'T', "Any", "SayTime"), merged);
        var j = Assert.Single(report.Imported, i => i.Source == "j");
        Assert.Equal("NextUnvisitedHeading", j.PreviousValue);
        Assert.Equal("NextHeading", j.Value);
    }

    [Fact]
    public void KeymapMerge_ReplacesTheUsersBindingOnTheKey_AndLeavesTheSameBindingAlone()
    {
        var report = new ImportReport(ImportSource.Nvda);
        IReadOnlyList<KeyBinding> user = [new(KeyModifiers.None, 'J', "Browse", "NextLink"), new(KeyModifiers.Alt, 'X', "Any", "SayTime")];
        var merged = KeymapImport.Merge(Layout, user,
        [
            new ImportedKey("j", NavigationCommand.NextHeading, KeyModifiers.None, 'J', "Browse"),
            new ImportedKey("h", NavigationCommand.NextHeading, KeyModifiers.None, 'H', "Browse"),
        ], report);

        Assert.Equal([new(KeyModifiers.Alt, 'X', "Any", "SayTime"), new(KeyModifiers.None, 'J', "Browse", "NextHeading")], merged);
        Assert.Equal("NextLink", report.Imported[0].PreviousValue);
        Assert.False(report.Imported[1].Changed);
    }

    [Fact]
    public void DictionaryMerge_KeepsVoxsEntries_AndReplacesTheSameRule()
    {
        var report = new ImportReport(ImportSource.Nvda);
        string existing = PronunciationDictionary.ToJson([new("lol", "L O L"), new("Vox", "vox", PronunciationMatch.Word, true)]);

        var json = DictionaryImport.Merge(existing,
            [new ImportedEntry("d:1", new PronunciationEntry("LOL", "laughing")), new ImportedEntry("d:2", new PronunciationEntry("brb", "be right back"))],
            "dictionaries/user.json", report);

        var entries = PronunciationDictionary.ReadEntries(json!);
        Assert.Equal(["laughing", "vox", "be right back"], entries.Select(e => e.Replacement));
        Assert.Equal("L O L", report.Imported[0].PreviousValue);
        Assert.Null(report.Imported[1].PreviousValue);
    }

    [Fact]
    public void DictionaryMerge_UnreadableFile_IsNeverReplaced()
    {
        var report = new ImportReport(ImportSource.Nvda);

        var json = DictionaryImport.Merge("{ not json", [new ImportedEntry("d:1", new PronunciationEntry("a", "b"))], "user.json", report);

        Assert.Null(json);
        Assert.Equal(ImportSkipReason.Unreadable, Assert.Single(report.Skipped).Reason);
    }

    [Fact]
    public void Writer_BacksUpAReplacedFile_AndWritesTheNewOne()
    {
        var report = new ImportReport(ImportSource.Nvda);
        string keymap = Path.Combine(_folder, "keymap.json");
        string dictionary = Path.Combine(_folder, "dictionaries", "user.json");
        File.WriteAllText(keymap, "old");
        var time = new DateTimeOffset(2026, 10, 12, 20, 25, 30, TimeSpan.Zero);

        ImportFileWriter.Write([new PlannedFile(keymap, "new"), new PlannedFile(dictionary, "entries")], report, time);

        Assert.Equal("new", File.ReadAllText(keymap));
        Assert.Equal("entries", File.ReadAllText(dictionary));
        string backup = Assert.Single(report.Backups);
        Assert.Equal(ImportFileWriter.BackupPath(keymap, time), backup);
        Assert.Equal("old", File.ReadAllText(backup));
        Assert.False(File.Exists(keymap + ".tmp"));
    }

    [Fact]
    public void Writer_UnchangedFile_IsNotBackedUp()
    {
        var report = new ImportReport(ImportSource.Nvda);
        string keymap = Path.Combine(_folder, "keymap.json");
        File.WriteAllText(keymap, "same");

        ImportFileWriter.Write([new PlannedFile(keymap, "same")], report, DateTimeOffset.Now);

        Assert.Empty(report.Backups);
    }
}
