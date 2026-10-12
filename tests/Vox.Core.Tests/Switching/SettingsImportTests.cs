using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Localization;
using Vox.Core.Speech;
using Vox.Core.Switching;
using Xunit;

namespace Vox.Core.Tests.Switching;

public sealed class SettingsImportTests : IDisposable
{
    private static readonly string Samples = Path.Combine(AppContext.BaseDirectory, "Switching", "Samples");
    private readonly string _vox = Path.Combine(Path.GetTempPath(), "vox-import-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_vox))
            Directory.Delete(_vox, recursive: true);
    }

    private ImportLocations Locations(string nvda = "nvda-folder", string jaws = "jaws-root") =>
        new(Path.Combine(Samples, nvda), Path.Combine(Samples, jaws), _vox, Path.Combine(_vox, "settings.json"));

    private static IReadOnlyList<string>? Voices(string engine) =>
        engine == SpeechEngineRegistry.OneCoreId ? ["Microsoft David", "Microsoft Zira"] : null;

    private SettingsImport Import(ImportLocations? locations = null) =>
        new(locations ?? Locations(), Voices, layout => KeyMap.LayoutBindings(_vox, layout),
            new FixedTime(new DateTimeOffset(2026, 10, 12, 20, 0, 0, TimeSpan.Zero)));

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void FindSources_NvdaThenJaws_WhenTheirSettingsAreThere()
    {
        Assert.Equal([ImportSource.Nvda, ImportSource.Jaws], Import().FindSources());
        Assert.Empty(Import(Locations("missing", "missing")).FindSources());
    }

    [Fact]
    public void Prepare_SourceNotThere_IsNull()
    {
        Assert.Null(Import(Locations("missing", "missing")).Prepare(ImportSource.Jaws, new VoxSettings()));
    }

    [Fact]
    public void Prepare_ChangesNothingOnDisk()
    {
        var result = Import().Prepare(ImportSource.Nvda, new VoxSettings());

        Assert.NotNull(result);
        Assert.NotEmpty(result.Files);
        Assert.False(Directory.Exists(_vox));
    }

    [Fact]
    public void Apply_BacksUpTheSettingsAndFilesItReplaces_AndSavesTheSettings()
    {
        Directory.CreateDirectory(_vox);
        File.WriteAllText(Path.Combine(_vox, "settings.json"), "old settings");
        File.WriteAllText(Path.Combine(_vox, KeyMap.UserFileName), KeyMap.ToJson([]));
        var current = new VoxSettings();
        var import = Import();
        var result = import.Prepare(ImportSource.Nvda, current)!;
        VoxSettings? saved = null;

        import.Apply(result, current, s => saved = s);

        Assert.Equal(result.Settings, saved);
        Assert.True(File.Exists(Path.Combine(_vox, PronunciationRule.FolderName, PronunciationRule.UserFileName)));
        Assert.Contains(KeyMap.ParseBindings(File.ReadAllText(Path.Combine(_vox, KeyMap.UserFileName))), b => b.Command == "SayAll");
        Assert.Equal(2, result.Report.Backups.Count);
        var settingsBackup = Assert.Single(result.Report.Backups, b => Path.GetFileName(b).StartsWith("settings.json.", StringComparison.Ordinal));
        Assert.Equal("old settings", File.ReadAllText(settingsBackup));
    }

    [Fact]
    public void Apply_SettingsUnchanged_AreNotSavedOrBackedUp()
    {
        Directory.CreateDirectory(_vox);
        File.WriteAllText(Path.Combine(_vox, "settings.json"), "settings");
        var current = new VoxSettings();
        var result = new SettingsImportResult(current, new ImportReport(ImportSource.Jaws));
        bool saved = false;

        Import().Apply(result, current, _ => saved = true);

        Assert.False(saved);
        Assert.Empty(result.Report.Backups);
    }

    [Fact]
    public void ForProfile_ReadsAndWritesUnderTheProfilesRoamingFolder()
    {
        var locations = ImportLocations.ForProfile(Path.Combine("C:", "Users", "sam"));
        var roaming = Path.Combine("C:", "Users", "sam", "AppData", "Roaming");

        Assert.Equal(Path.Combine(roaming, "nvda"), locations.NvdaFolder);
        Assert.Equal(Path.Combine(roaming, "Freedom Scientific", "JAWS"), locations.JawsRoot);
        Assert.Equal(Path.Combine(roaming, "Vox", "settings.json"), locations.SettingsPath);
    }

    [Theory]
    [InlineData("nvda", ImportSource.Nvda)]
    [InlineData(" JAWS ", ImportSource.Jaws)]
    [InlineData("narrator", null)]
    public void ParseSource_TakesTheCommandLinesName(string name, ImportSource? expected) =>
        Assert.Equal(expected, SettingsImport.ParseSource(name));

    [Fact]
    public void Cli_ImportsAndListsOnlyWhatWasImported()
    {
        var output = new StringWriter();
        VoxSettings? saved = null;

        int code = SettingsImportCli.Run(Import(), ImportSource.Jaws, () => new VoxSettings(), s => saved = s, output);

        Assert.Equal(0, code);
        Assert.Equal(PunctuationLevel.Most, saved!.PunctuationLevel);
        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(lines, l => l.StartsWith("Imported PunctuationLevel = Most", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("MyCustomScript", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("imported from Jaws", StringComparison.Ordinal));
    }

    [Fact]
    public void Cli_NothingToImport_Fails()
    {
        var output = new StringWriter();

        int code = SettingsImportCli.Run(Import(Locations("missing", "missing")), ImportSource.Nvda, () => new VoxSettings(), _ => { }, output);

        Assert.Equal(1, code);
    }

    [Fact]
    public void ReportText_ListsWhatWasBroughtOverAndWhatWasNot()
    {
        var report = new ImportReport(ImportSource.Nvda);
        report.AddFile("nvda.ini");
        report.AddImported("speech/oneCore/rate", "SpeechRateWpm", 310, 250);
        report.AddImported("gestures", "keymap.json: Insert+A (Any)", "SayAll", null);
        report.AddSkipped("speech/synth", "audiotts", ImportSkipReason.NoCounterpart);
        report.AddBackup("keymap.json.20261012-200000.bak");

        var text = ImportReportText.Format(report, applied: false);

        Assert.StartsWith("Settings from NVDA", text);
        Assert.Contains("Nothing has been changed yet", text);
        Assert.Contains("SpeechRateWpm: 310, was 250. From speech/oneCore/rate", text);
        Assert.Contains("keymap.json: Insert+A (Any): SayAll. From gestures", text);
        Assert.Contains("speech/synth: audiotts. Vox has nothing like it", text);
        Assert.Contains("keymap.json.20261012-200000.bak", text);
    }

    [Fact]
    public void EveryReasonAndSource_HasItsString()
    {
        foreach (var reason in Enum.GetValues<ImportSkipReason>())
            Assert.True(VoxStrings.Has($"Import.Reason.{reason}"), reason.ToString());
        foreach (var source in Enum.GetValues<ImportSource>())
            Assert.True(VoxStrings.Has($"Import.Source.{source}"), source.ToString());
    }
}
