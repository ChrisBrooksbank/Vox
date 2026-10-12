using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Speech;
using Vox.Core.Switching;
using Xunit;

namespace Vox.Core.Tests.Switching;

public sealed class NvdaSettingsImporterTests
{
    private static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "Switching", "Samples", name);

    // OneCore and SAPI installed, eSpeak NG not
    private static NvdaSettingsImporter Importer() => new(engine => engine switch
    {
        SpeechEngineRegistry.OneCoreId => ["Microsoft David", "Microsoft Hazel", "Microsoft Zira"],
        SpeechEngineRegistry.SapiId => ["Microsoft David Desktop", "Microsoft Zira Desktop"],
        _ => null,
    });

    [Fact]
    public void SampleFile_BringsOverSpeechKeyboardBrowseModeAndBraille()
    {
        var result = Importer().ImportFile(Sample("nvda-onecore.ini"), new VoxSettings());
        var s = result.Settings;

        Assert.Equal(SpeechEngineRegistry.OneCoreId, s.SpeechEngine);
        Assert.Equal("Microsoft Hazel", s.VoiceName);
        Assert.Equal(310, s.SpeechRateWpm);
        Assert.Equal(40, s.SpeechPitch);
        Assert.Equal(90, s.SpeechVolume);
        Assert.Equal(PunctuationLevel.Most, s.PunctuationLevel);
        Assert.Equal(CapitalIndication.SayCap, s.CapitalsForCharacters);
        Assert.Equal(TypingEchoMode.Words, s.TypingEchoMode);
        Assert.Equal(KeyboardLayout.Laptop, s.KeyboardLayout);
        Assert.Equal(ModifierKey.CapsLock, s.ModifierKey);
        Assert.Equal(120, s.MaxLineLength);
        Assert.False(s.ScreenLayout);
        Assert.Equal("BRLTTY", s.BrailleDisplay);
        Assert.Equal("en-us-g2.ctb", s.BrailleTable);
        Assert.Equal(BrailleTether.Review, s.BrailleTether);
        Assert.Equal(BrailleFocusContext.ChangedContext, s.BrailleFocusContext);
        Assert.Equal(BrailleCursorShape.AllDots, s.BrailleCursorShape);
        Assert.False(s.BrailleCursorBlink);
        Assert.Equal(800, s.BrailleCursorBlinkRateMs);
        Assert.Equal(6, s.BrailleMessageTimeoutSeconds);
        Assert.False(s.BrailleWordWrap);

        Assert.Empty(result.Report.Skipped);
        Assert.Equal(ImportSource.Nvda, result.Report.Source);
        Assert.Equal([Sample("nvda-onecore.ini")], result.Report.Files);
        Assert.True(result.Report.HasChanges);
    }

    [Fact]
    public void Report_SaysWhereEachValueCameFromAndWhatItReplaced()
    {
        var result = Importer().ImportFile(Sample("nvda-onecore.ini"), new VoxSettings { SpeechRateWpm = 250 });

        var rate = Assert.Single(result.Report.Imported, i => i.Target == nameof(VoxSettings.SpeechRateWpm));
        Assert.Equal("speech/oneCore/rate", rate.Source);
        Assert.Equal("310", rate.Value);
        Assert.Equal("250", rate.PreviousValue);
        Assert.True(rate.Changed);

        var punctuation = Assert.Single(result.Report.Imported, i => i.Target == nameof(VoxSettings.PunctuationLevel));
        Assert.Equal("Most", punctuation.Value);
        Assert.Equal("Some", punctuation.PreviousValue);
    }

    [Fact]
    public void OnlyTheChosenSynthesizersSection_IsRead()
    {
        // The sample's eSpeak section has rate 20, which would be slower
        var result = Importer().ImportFile(Sample("nvda-onecore.ini"), new VoxSettings());

        Assert.DoesNotContain(result.Report.Imported, i => i.Source.StartsWith("speech/espeak", StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedFile_ReadsWhatItCan_AndReportsTheRest()
    {
        var current = new VoxSettings();
        var result = Importer().ImportFile(Sample("nvda-malformed.ini"), current);
        var s = result.Settings;
        var skipped = result.Report.Skipped;

        // Bad lines, by line number
        Assert.Contains(skipped, i => i.Reason == ImportSkipReason.Malformed && i.Source == "nvda-malformed.ini:3");
        Assert.Contains(skipped, i => i.Reason == ImportSkipReason.Malformed && i.Source == "nvda-malformed.ini:4");
        Assert.Contains(skipped, i => i.Reason == ImportSkipReason.Malformed && i.Source == "nvda-malformed.ini:24");

        // eSpeak NG isn't installed: neither it nor its voice is chosen; its rate still applies (boosted)
        Assert.Contains(skipped, i => i.Source == "speech/synth" && i.Reason == ImportSkipReason.NotInstalled);
        Assert.Contains(skipped, i => i.Source == "speech/espeak/voice" && i.Reason == ImportSkipReason.NotInstalled);
        Assert.Null(s.SpeechEngine);
        Assert.Null(s.VoiceName);
        Assert.Equal(795, s.SpeechRateWpm);

        // Values that can't be read keep Vox's; out of range ones are limited
        Assert.Contains(skipped, i => i.Source == "speech/symbolLevel" && i.Value == "lots" && i.Reason == ImportSkipReason.InvalidValue);
        Assert.Contains(skipped, i => i.Source == "speech/espeak/pitch" && i.Reason == ImportSkipReason.InvalidValue);
        Assert.Equal(current.PunctuationLevel, s.PunctuationLevel);
        Assert.Equal(current.SpeechPitch, s.SpeechPitch);
        Assert.Equal(100, s.SpeechVolume);

        // Numeric typing echo (newer NVDA): characters always, words off
        Assert.Equal(TypingEchoMode.Characters, s.TypingEchoMode);

        // CapsLock as well as Insert: Vox keeps Insert and says so
        Assert.Contains(skipped, i => i.Source == "keyboard/useCapsLockAsNVDAModifierKey" && i.Reason == ImportSkipReason.Unsupported);
        Assert.Equal(ModifierKey.Insert, s.ModifierKey);

        // Braille: no driver, no table, no such tether; messages off
        Assert.Contains(skipped, i => i.Source == "braille/display" && i.Reason == ImportSkipReason.NoCounterpart);
        Assert.Contains(skipped, i => i.Source == "braille/translationTable" && i.Reason == ImportSkipReason.Unsupported);
        Assert.Contains(skipped, i => i.Source == "braille/tetherTo" && i.Reason == ImportSkipReason.Unsupported);
        Assert.Equal(current.BrailleDisplay, s.BrailleDisplay);
        Assert.Equal(current.BrailleTable, s.BrailleTable);
        Assert.Equal(0, s.BrailleMessageTimeoutSeconds);
    }

    [Fact]
    public void WhatTheFileDoesNotSet_KeepsVoxsValue()
    {
        var current = new VoxSettings { SpeechRateWpm = 333, TypingEchoMode = TypingEchoMode.None, BrailleTable = "de-g2.ctb" };

        var result = Importer().Import("[speech]\n\tsymbolLevel = 0\n", current);

        Assert.Equal(current with { PunctuationLevel = PunctuationLevel.None }, result.Settings);
        Assert.Single(result.Report.Imported);
        Assert.Empty(result.Report.Skipped);
    }

    [Fact]
    public void SameValueAsVox_IsReportedButNotAChange()
    {
        var result = Importer().Import("[speech]\n\tsymbolLevel = 100\n", new VoxSettings());

        Assert.False(Assert.Single(result.Report.Imported).Changed);
        Assert.False(result.Report.HasChanges);
    }

    [Fact]
    public void SynthesizerVoxDoesNotHave_IsSkipped_ButItsRateStillApplies()
    {
        var result = Importer().Import("[speech]\nsynth = ibmeci\n[[ibmeci]]\nvoice = 1\nrate = 50\n", new VoxSettings());

        Assert.Contains(result.Report.Skipped, i => i.Source == "speech/synth" && i.Reason == ImportSkipReason.NoCounterpart);
        Assert.Contains(result.Report.Skipped, i => i.Source == "speech/ibmeci/voice" && i.Reason == ImportSkipReason.NoCounterpart);
        Assert.Equal(180, result.Settings.SpeechRateWpm);
    }

    [Fact]
    public void AutomaticSynthesizer_ReadsTheOneCoreSection()
    {
        var result = Importer().Import("[speech]\n[[oneCore]]\nvoice = Microsoft Zira\n", new VoxSettings());

        Assert.Equal("Microsoft Zira", result.Settings.VoiceName);
        Assert.Null(result.Settings.SpeechEngine);
    }

    [Fact]
    public void SapiSynthesizer_ChoosesSapiAndItsVoice()
    {
        var result = Importer().Import(
            "[speech]\nsynth = sapi5\n[[sapi5]]\nvoice = HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Speech\\Voices\\Tokens\\TTS_MS_EN-US_ZIRA_11.0\n",
            new VoxSettings { SpeechEngine = SpeechEngineRegistry.OneCoreId, VoiceName = "Microsoft David" });

        Assert.Equal(SpeechEngineRegistry.SapiId, result.Settings.SpeechEngine);
        Assert.Equal("Microsoft Zira Desktop", result.Settings.VoiceName);
    }

    [Theory]
    [InlineData("sayCapForCapitals = False\nbeepForCapitals = True", CapitalIndication.Beep)]
    [InlineData("capPitchChange = 0", CapitalIndication.Off)]
    [InlineData("capPitchChange = 20", CapitalIndication.Pitch)]
    [InlineData("sayCapForCapitals = False", CapitalIndication.Pitch)]
    public void Capitals_AreTheCharacterIndication(string lines, CapitalIndication expected)
    {
        var result = Importer().Import("[speech]\n[[oneCore]]\n" + lines + "\n", new VoxSettings { CapitalsForCharacters = CapitalIndication.SayCap });

        Assert.Equal(expected, result.Settings.CapitalsForCharacters);
    }

    [Theory]
    [InlineData(0, PunctuationLevel.None)]
    [InlineData(100, PunctuationLevel.Some)]
    [InlineData(200, PunctuationLevel.Most)]
    [InlineData(300, PunctuationLevel.All)]
    [InlineData(1000, PunctuationLevel.All)]
    public void SymbolLevels_MapToPunctuationLevels(int level, PunctuationLevel expected) =>
        Assert.Equal(expected, NvdaSettingsImporter.PunctuationFor(level));

    [Theory]
    [InlineData("oneCore", 50, false, 180)]
    [InlineData("sapi5", 100, false, 540)]
    [InlineData("sapi5", 0, false, ISpeechEngine.MinSupportedWpm)]
    [InlineData("espeak", 100, false, 450)]
    [InlineData("espeak", 100, true, ISpeechEngine.MaxSupportedWpm)]
    public void Rates_BecomeWordsPerMinuteVoxSupports(string synth, int rate, bool boost, int expected) =>
        Assert.Equal(expected, NvdaSettingsImporter.RateToWpm(synth, rate, boost));

    [Fact]
    public void UnreadableFile_IsReported_AndNothingChanges()
    {
        var current = new VoxSettings();

        var result = Importer().ImportFile(Path.Combine(Path.GetTempPath(), "missing_" + Guid.NewGuid().ToString("N"), "nvda.ini"), current);

        Assert.Same(current, result.Settings);
        Assert.Equal(ImportSkipReason.Unreadable, Assert.Single(result.Report.Skipped).Reason);
    }

    [Fact]
    public void Folder_BringsOverSettingsKeysAndDictionaries_AsPlannedFiles()
    {
        string vox = Path.Combine(Path.GetTempPath(), "vox-user-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(vox, PronunciationRule.FolderName));
        try
        {
            File.WriteAllText(Path.Combine(vox, PronunciationRule.FolderName, PronunciationRule.UserFileName),
                PronunciationDictionary.ToJson([new PronunciationEntry("lol", "L O L", PronunciationMatch.Anywhere)]));
            string nvda = Path.Combine(AppContext.BaseDirectory, "Switching", "Samples", "nvda-folder");

            var result = Importer().ImportFolder(nvda, new VoxSettings(), vox, layout => KeyMap.LayoutBindings(vox, layout));

            Assert.Equal("Microsoft David", result.Settings.VoiceName);
            var keymap = Assert.Single(result.Files, f => f.Path == Path.Combine(vox, KeyMap.UserFileName));
            var bindings = KeyMap.ParseBindings(keymap.Content);
            Assert.Contains(new KeyBinding(KeyModifiers.Insert, 'A', "Any", "SayAll"), bindings);
            Assert.Contains(new KeyBinding(KeyModifiers.None, 'J', "Browse", "NextHeading"), bindings);

            var user = Assert.Single(result.Files, f => f.Path.EndsWith(PronunciationRule.UserFileName, StringComparison.Ordinal));
            var entries = PronunciationDictionary.ReadEntries(user.Content);
            Assert.Equal("laughing out loud", Assert.Single(entries, e => e.Pattern == "lol").Replacement);
            Assert.Contains(entries, e => e.Pattern == "Vox");

            var david = Assert.Single(result.Files, f => f.Path == PronunciationRule.VoiceFile(Path.Combine(vox, PronunciationRule.FolderName), "Microsoft David"));
            Assert.Equal("jif", Assert.Single(PronunciationDictionary.ReadEntries(david.Content)).Replacement);
            Assert.Equal(3, result.Files.Count);

            // Julie isn't installed; NVDA's audiotts has no counterpart
            Assert.Contains(result.Report.Skipped, s => s.Value!.EndsWith("frFR_JulieM", StringComparison.Ordinal) && s.Reason == ImportSkipReason.NotInstalled);
            Assert.Contains(result.Report.Skipped, s => s.Value == "Anna" && s.Reason == ImportSkipReason.NoCounterpart);
            Assert.Contains(result.Report.Imported, i => i.Target == "dictionaries/user.json: lol" && i.PreviousValue == "L O L");
            Assert.Equal(6, result.Report.Files.Count);
            // Nothing written yet
            Assert.False(File.Exists(Path.Combine(vox, KeyMap.UserFileName)));
        }
        finally
        {
            Directory.Delete(vox, recursive: true);
        }
    }
}
