using Vox.Core.Configuration;
using Vox.Core.Speech;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class PunctuationRuleTests
{
    private static readonly SymbolDictionary Symbols = SymbolDictionary.LoadBuiltIn();

    private static string Speak(string text, PunctuationLevel level) =>
        new PunctuationRule(Symbols, () => level).Apply(text, new Utterance(text, SpeechPriority.Normal));

    [Theory]
    [InlineData(PunctuationLevel.None, "Tom Jerry cartoon, 50 off!")]
    [InlineData(PunctuationLevel.Some, "Tom and Jerry cartoon, 50 percent off!")]
    [InlineData(PunctuationLevel.Most, "Tom and Jerry left paren cartoon right paren, 50 percent off!")]
    [InlineData(PunctuationLevel.All, "Tom and Jerry left paren cartoon right paren comma 50 percent off bang")]
    public void EachLevel_SpeaksItsSymbols(PunctuationLevel level, string expected)
    {
        Assert.Equal(Collapse(expected), Speak("Tom & Jerry (cartoon), 50% off!", level));
    }

    [Theory]
    [InlineData(".", "dot")]
    [InlineData("&", "and")]
    [InlineData(" ( ", "left paren")]
    [InlineData("—", "em dash")]
    public void ASymbolOnItsOwn_IsAlwaysNamed(string text, string expected)
    {
        Assert.Equal(expected, Speak(text, PunctuationLevel.None));
    }

    [Fact]
    public void DotsAndCommasInNumbers_AreLeftAlone()
    {
        Assert.Equal("pi is 3.14 and a thousand is 1,000", Speak("pi is 3.14 and a thousand is 1,000", PunctuationLevel.All));
    }

    [Fact]
    public void PreservedSymbols_AreKeptForTheirPause_WhenNotSpoken()
    {
        Assert.Equal("Wait: what? Yes.", Speak("Wait: what? Yes.", PunctuationLevel.Some));
        Assert.Equal("Wait colon what? Yes.", Speak("Wait: what? Yes.", PunctuationLevel.Most));
    }

    [Fact]
    public void LongerSymbolsWin()
    {
        Assert.Equal("And then dot dot dot", Speak("And then…", PunctuationLevel.Most));
    }

    [Fact]
    public void TextWithoutSymbols_IsUnchanged()
    {
        const string text = "Nothing  to  see here";
        Assert.Same(text, Speak(text, PunctuationLevel.All));
    }

    [Fact]
    public void TheBuiltInDictionary_NamesEverySymbol()
    {
        Assert.True(Symbols.Entries.Count >= 50);
        Assert.All(Symbols.Entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Name)));
        Assert.True(Symbols.TryGet("&", out var ampersand));
        Assert.Equal(PunctuationLevel.Some, ampersand.Level);
    }

    private static string Collapse(string text) => System.Text.RegularExpressions.Regex.Replace(text, " {2,}", " ");

    [Theory]
    [InlineData("😀", "grinning face")]
    [InlineData("👋🏽", "waving hand: medium skin tone")]
    [InlineData("❤️", "red heart")] // with the variation selector
    [InlineData("🇬🇧", "flag: United Kingdom")]
    public void Emoji_OnTheirOwn_AreNamed(string text, string expected)
    {
        Assert.Equal(expected, Speak(text, PunctuationLevel.None));
    }

    [Fact]
    public void Emoji_InText_AreSpokenAtEveryLevel()
    {
        Assert.Equal("Thanks grinning face see you", Speak("Thanks 😀 see you", PunctuationLevel.None));
        Assert.Equal("Thanks grinning face", Speak("Thanks😀", PunctuationLevel.None));
    }

    [Fact]
    public void EmojiSequences_AreNamedWhole()
    {
        // Man, woman, girl joined by zero-width joiners
        Assert.Equal("our family: man, woman, girl", Speak("our \U0001F468\u200D\U0001F469\u200D\U0001F467", PunctuationLevel.None));
    }

    [Fact]
    public void OtherCldrSymbols_AreSpokenFromSome()
    {
        Assert.Equal("next page", Speak("next → page", PunctuationLevel.None));
        Assert.Equal("next right-pointing arrow page", Speak("next → page", PunctuationLevel.Some));
    }

    [Fact]
    public void LongText_IsQuick()
    {
        var text = string.Concat(Enumerable.Repeat("The quick brown fox, it said (twice): jumps! 😀 ", 400));
        var rule = new PunctuationRule(Symbols, () => PunctuationLevel.Most);
        rule.Apply(text, new Utterance(text, SpeechPriority.Normal));

        // The best of a few runs, so other tests running at the same time don't make it fail
        long best = long.MaxValue;
        for (int run = 0; run < 5; run++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            rule.Apply(text, new Utterance(text, SpeechPriority.Normal));
            best = Math.Min(best, watch.ElapsedMilliseconds);
        }

        Assert.True(best < 50, $"Took {best} ms");
    }

    [Theory]
    [InlineData("----", PunctuationLevel.Most, "4 dashes")]
    [InlineData("Title\n==========", PunctuationLevel.Some, "Title\n 10 equals")]
    [InlineData("wait!!!!!", PunctuationLevel.All, "wait 5 bangs")]
    [InlineData("😀😀😀😀", PunctuationLevel.None, "4 grinning faces")]
    [InlineData("a---b", PunctuationLevel.Most, "a dash dash dash b")]
    public void RunsOfASymbol_AreCounted(string text, PunctuationLevel level, string expected)
    {
        Assert.Equal(expected, Speak(text, level));
    }

    [Fact]
    public void RunsOfASymbolNotSpokenAtTheLevel_StaySilent()
    {
        Assert.Equal("Title", Speak("Title ----------", PunctuationLevel.Some).TrimEnd('-', ' '));
        Assert.Equal("Title", Speak("Title __________", PunctuationLevel.None));
    }

    [Theory]
    [InlineData("dash", "dashes")]
    [InlineData("question mark", "question marks")]
    [InlineData("hash", "hashes")]
    [InlineData("star", "stars")]
    [InlineData("equals", "equals")]
    [InlineData("plus", "pluses")]
    public void Plural(string name, string expected)
    {
        Assert.Equal(expected, PunctuationRule.Plural(name));
    }
}
