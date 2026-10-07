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

        var watch = System.Diagnostics.Stopwatch.StartNew();
        rule.Apply(text, new Utterance(text, SpeechPriority.Normal));

        Assert.True(watch.ElapsedMilliseconds < 50, $"Took {watch.ElapsedMilliseconds} ms");
    }
}
