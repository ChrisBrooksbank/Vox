using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Speech;
using Vox.Core.Switching;
using Xunit;

namespace Vox.Core.Tests.Switching;

public sealed class JawsSettingsImporterTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Switching", "Samples", "jaws-root");
    private static readonly string Settings = Path.Combine(Root, "2025", "Settings", "enu");

    private static string Sample(string name) => File.ReadAllText(Path.Combine(Settings, name));

    private static JawsSettingsImporter Importer() => new(engine => engine switch
    {
        SpeechEngineRegistry.OneCoreId => ["Microsoft David", "Microsoft Zira"],
        _ => null,
    });

    [Fact]
    public void FindSettingsFolder_TakesTheNewestVersion_InEnglish()
    {
        Assert.Equal(Settings, JawsSettingsImporter.FindSettingsFolder(Root));
        Assert.Null(JawsSettingsImporter.FindSettingsFolder(Path.Combine(Root, "missing")));
    }

    [Fact]
    public void Configuration_BringsOverRatePunctuationAndTypingEcho()
    {
        var report = new ImportReport(ImportSource.Jaws);

        var settings = JawsSettingsImporter.ImportConfiguration(Sample("default.jcf"), new VoxSettings(), report);

        Assert.Equal(NvdaSettingsImporter.RateToWpm("sapi5", 62, false), settings.SpeechRateWpm);
        Assert.Equal(PunctuationLevel.Most, settings.PunctuationLevel);
        Assert.Equal(TypingEchoMode.Words, settings.TypingEchoMode);
        Assert.Contains(report.Imported, i => i.Source == "Options/Punctuation" && i.Value == "Most");
        Assert.Empty(report.Skipped);
    }

    [Fact]
    public void Configuration_InvalidValues_AreReported()
    {
        var report = new ImportReport(ImportSource.Jaws);
        var current = new VoxSettings();

        var settings = JawsSettingsImporter.ImportConfiguration("[Options]\nRate=fast\nPunctuation=9\n", current, report);

        Assert.Equal(current, settings);
        Assert.Equal(2, report.Skipped.Count(s => s.Reason == ImportSkipReason.InvalidValue));
    }

    [Fact]
    public void KeyMap_MapsScriptsVoxHas_InCommonAndTheLayoutsSection()
    {
        var report = new ImportReport(ImportSource.Jaws);

        var keys = JawsSettingsImporter.ParseKeyMap(Sample("default.jkm"), KeyboardLayout.Desktop, report);

        Assert.Contains(keys, k => k.Command == NavigationCommand.SayTime && k.Modifiers == (KeyModifiers.Insert | KeyModifiers.Ctrl) && k.VkCode == 'T');
        Assert.Contains(keys, k => k.Command == NavigationCommand.SayTitle && k.Modifiers == KeyModifiers.Insert && k.VkCode == 'W');
        Assert.Contains(keys, k => k.Command == NavigationCommand.ReadCurrentWord && k.VkCode == 101);
        Assert.Equal(3, keys.Count);

        Assert.Contains(report.Skipped, s => s.Value == "SayAll" && s.Source.Contains("Common Keys") && s.Reason == ImportSkipReason.Unsupported);
        Assert.Contains(report.Skipped, s => s.Value == "MyCustomScript" && s.Reason == ImportSkipReason.NoCounterpart);
        Assert.Contains(report.Skipped, s => s.Value == "SayLine" && s.Reason == ImportSkipReason.InvalidValue);
        Assert.Contains(report.Skipped, s => s.Source.Contains("Laptop Keys") && s.Reason == ImportSkipReason.Unsupported);
        Assert.Contains(report.Skipped, s => s.Source.Contains("Braille Keys") && s.Reason == ImportSkipReason.NoCounterpart);
        Assert.Contains(report.Skipped, s => s.Source == "default.jkm:14" && s.Reason == ImportSkipReason.Malformed);
    }

    [Fact]
    public void KeyMap_LaptopLayout_TakesTheLaptopSection()
    {
        var keys = JawsSettingsImporter.ParseKeyMap(Sample("default.jkm"), KeyboardLayout.Laptop, new ImportReport(ImportSource.Jaws));

        Assert.Contains(keys, k => k.Command == NavigationCommand.ReadCurrentWord && k.Modifiers == KeyModifiers.Insert && k.VkCode == 'K');
        Assert.DoesNotContain(keys, k => k.VkCode == 101);
    }

    [Theory]
    [InlineData("Insert+Control+F", KeyModifiers.Insert | KeyModifiers.Ctrl, 'F')]
    [InlineData("Shift+Semicolon", KeyModifiers.Shift, 186)]
    [InlineData("Alt+Shift+DownArrow", KeyModifiers.Alt | KeyModifiers.Shift, 40)]
    [InlineData("JAWSKey+F12", KeyModifiers.Insert, 123)]
    [InlineData("Insert+NumPadEnter", KeyModifiers.Insert, NumpadKeys.NumpadEnter)]
    public void Key_ParsesModifiersAndKey(string text, KeyModifiers modifiers, int vkCode)
    {
        Assert.True(JawsSettingsImporter.TryParseKey(text, out var m, out int vk, out _));
        Assert.Equal(modifiers, m);
        Assert.Equal(vkCode, vk);
    }

    [Fact]
    public void Dictionary_ReadsEntries_WithVoicesAndWildcards()
    {
        var report = new ImportReport(ImportSource.Jaws);

        var entries = JawsSettingsImporter.ParseDictionary(Sample("default.jdf"), "default.jdf", report);

        Assert.Equal(new PronunciationEntry("lol", "laughing out loud", PronunciationMatch.Word), entries[0].Entry.Entry);
        Assert.Null(entries[0].Voice);
        Assert.Null(entries[1].Voice);
        Assert.Equal("Eloquence Reed", entries[3].Voice);
        Assert.Equal(new PronunciationEntry("a.b", "ab slash", PronunciationMatch.Word), entries[5].Entry.Entry);
        Assert.Equal(6, entries.Count);
        Assert.Equal(2, report.Skipped.Count(s => s.Reason == ImportSkipReason.Malformed));

        var wildcard = new PronunciationDictionary([entries[2].Entry.Entry]);
        Assert.Equal("ING ending and ING ending", wildcard.Apply("running and Singing"));
    }

    [Fact]
    public void Folder_BringsOverSettingsKeysAndDictionaries_AsPlannedFiles()
    {
        string vox = Path.Combine(Path.GetTempPath(), "vox-user-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = Importer().ImportFolder(Settings, new VoxSettings(), vox, layout => KeyMap.LayoutBindings(vox, layout));

            Assert.Equal(ImportSource.Jaws, result.Report.Source);
            Assert.Equal(PunctuationLevel.Most, result.Settings.PunctuationLevel);

            var keymap = KeyMap.ParseBindings(Assert.Single(result.Files, f => f.Path == Path.Combine(vox, KeyMap.UserFileName)).Content);
            Assert.Contains(new KeyBinding(KeyModifiers.None, 'J', "Browse", "NextHeading"), keymap);
            Assert.Contains(keymap, b => b.Command == "SayTime" && b.Modifiers == (KeyModifiers.Insert | KeyModifiers.Ctrl) && b.VkCode == 'T');
            Assert.Contains(result.Report.Skipped, s => s.Source == "outlook.jkm" && s.Reason == ImportSkipReason.NoCounterpart);

            string dictionaries = Path.Combine(vox, PronunciationRule.FolderName);
            var user = PronunciationDictionary.ReadEntries(Assert.Single(result.Files, f => f.Path == Path.Combine(dictionaries, PronunciationRule.UserFileName)).Content);
            Assert.Equal(["lol", "GIF", @"\w*ing(?!\w)", "a.b"], user.Select(e => e.Pattern));
            var zira = PronunciationDictionary.ReadEntries(Assert.Single(result.Files, f => f.Path == PronunciationRule.VoiceFile(dictionaries, "Microsoft Zira")).Content);
            Assert.Equal("color", Assert.Single(zira).Replacement);
            Assert.Contains(result.Report.Skipped, s => s.Value == "Vox" && s.Reason == ImportSkipReason.NotInstalled && s.Detail == "Eloquence Reed");
            Assert.Equal(3, result.Files.Count);
        }
        finally
        {
            if (Directory.Exists(vox))
                Directory.Delete(vox, recursive: true);
        }
    }
}
