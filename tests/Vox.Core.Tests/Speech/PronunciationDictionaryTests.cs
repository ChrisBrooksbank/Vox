using Vox.Core.Speech;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class PronunciationDictionaryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VoxDictionaries_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    private static string Apply(string text, params PronunciationEntry[] entries) => new PronunciationDictionary(entries).Apply(text);

    [Fact]
    public void Word_MatchesWholeWordsOnly_IgnoringCase()
    {
        var entry = new PronunciationEntry("cat", "kitty");

        Assert.Equal("kitty, kitty and concatenate", Apply("cat, CAT and concatenate", entry));
    }

    [Fact]
    public void Word_WorksForPatternsEndingInSymbols()
    {
        Assert.Equal("I like C sharp.", Apply("I like C#.", new PronunciationEntry("C#", "C sharp")));
    }

    [Fact]
    public void Anywhere_MatchesInsideWords()
    {
        Assert.Equal("concatenate", Apply("concatenate", new PronunciationEntry("cat", "dog", PronunciationMatch.Word)));
        Assert.Equal("condogenate", Apply("concatenate", new PronunciationEntry("cat", "dog", PronunciationMatch.Anywhere)));
    }

    [Fact]
    public void CaseSensitive_MatchesOnlyThatCase()
    {
        var entry = new PronunciationEntry("US", "U S", CaseSensitive: true);

        Assert.Equal("U S and us", Apply("US and us", entry));
    }

    [Fact]
    public void Regex_CanUseGroups()
    {
        var entry = new PronunciationEntry(@"(\d+)px\b", "$1 pixels", PronunciationMatch.Regex);

        Assert.Equal("width 20 pixels", Apply("width 20px", entry));
    }

    [Fact]
    public void LiteralReplacements_KeepDollarSigns()
    {
        Assert.Equal("costs $1", Apply("costs price", new PronunciationEntry("price", "$1")));
    }

    [Fact]
    public void RulesApplyInOrder()
    {
        Assert.Equal("c", Apply("a", new PronunciationEntry("a", "b"), new PronunciationEntry("b", "c")));
    }

    [Fact]
    public void InvalidEntries_AreSkipped_WithWarnings()
    {
        var dictionary = new PronunciationDictionary(
            [new("(unclosed", "x", PronunciationMatch.Regex), new("", "x"), new("ok", "fine")], out var warnings);

        Assert.Equal(1, dictionary.Count);
        Assert.Equal(2, warnings.Count);
        Assert.Equal("fine", dictionary.Apply("ok"));
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var json = PronunciationDictionary.ToJson([new("Vox", "Vocks"), new(@"\bQ(\d)\b", "quarter $1", PronunciationMatch.Regex, true)]);

        var dictionary = PronunciationDictionary.FromJson(json, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal("Vocks in quarter 3", dictionary.Apply("vox in Q3"));
    }

    [Fact]
    public void BuiltInDefault_Loads()
    {
        var dictionary = PronunciationRule.LoadBuiltInDefault();

        Assert.True(dictionary.Count > 0);
        Assert.Equal("N V D A users", dictionary.Apply("NVDA users"));
    }

    private void Write(string relativePath, params PronunciationEntry[] entries)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, PronunciationDictionary.ToJson(entries));
    }

    [Fact]
    public void Rule_AppliesUserThenVoiceThenDefault()
    {
        Write(PronunciationRule.UserFileName, new PronunciationEntry("tomato", "tomayto"));
        Write(Path.Combine(PronunciationRule.VoicesFolderName, "Microsoft Aria.json"), new PronunciationEntry("tomayto", "tomahto"));
        var voice = "Microsoft Aria";
        using var rule = new PronunciationRule(new PronunciationDictionary([new("tomahto", "TOMAHTO", CaseSensitive: true)]),
            _directory, () => voice);

        Assert.Equal("TOMAHTO", rule.Apply("tomato", new Utterance("tomato", SpeechPriority.Normal)));

        voice = "Microsoft Libby";
        Assert.Equal("tomayto", rule.Apply("tomato", new Utterance("tomato", SpeechPriority.Normal)));
    }

    [Fact]
    public void Rule_Reload_PicksUpANewRule()
    {
        using var rule = new PronunciationRule(PronunciationDictionary.Empty, _directory, () => null);
        Assert.Equal("gif", rule.Apply("gif", new Utterance("gif", SpeechPriority.Normal)));

        Write(PronunciationRule.UserFileName, new PronunciationEntry("gif", "jif"));
        rule.Reload();

        Assert.Equal("jif", rule.Apply("gif", new Utterance("gif", SpeechPriority.Normal)));
    }

    [Fact]
    public void Rule_WithoutADirectory_UsesOnlyTheDefault()
    {
        using var rule = new PronunciationRule(new PronunciationDictionary([new("a", "b")]), null, () => "Any voice");

        Assert.Equal("b", rule.Apply("a", new Utterance("a", SpeechPriority.Normal)));
    }

    [Fact]
    public void Rule_UnreadableFile_IsIgnored()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, PronunciationRule.UserFileName), "{ not json");

        using var rule = new PronunciationRule(PronunciationDictionary.Empty, _directory, () => null);

        Assert.Equal("text", rule.Apply("text", new Utterance("text", SpeechPriority.Normal)));
    }

    [Fact]
    public void VoiceFile_ReplacesCharactersNotAllowedInFileNames()
    {
        var path = PronunciationRule.VoiceFile(_directory, "Voice: one/two");

        Assert.Equal(Path.Combine(_directory, "voices"), Path.GetDirectoryName(path));
        Assert.DoesNotContain('/', Path.GetFileName(path));
    }
}
