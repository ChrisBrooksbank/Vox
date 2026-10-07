using Moq;
using Vox.Core.Configuration;
using Vox.Core.Speech;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class LanguageRuleTests
{
    private static readonly SpeechVoice[] Voices =
    [
        new("Microsoft Libby", "en-GB"),
        new("Microsoft Aria", "en-US"),
        new("Microsoft Denise", "fr-FR"),
        new("Microsoft Sylvie", "fr-CA"),
        new("Microsoft Katja", "de-DE"),
    ];

    private readonly Mock<ISpeechEngine> _engine = new();
    private VoxSettings _settings = new();
    private readonly LanguageRule _rule;

    public LanguageRuleTests()
    {
        _engine.Setup(e => e.GetVoiceDetails()).Returns(Voices);
        _engine.Setup(e => e.CurrentVoice).Returns("Microsoft Libby");
        _rule = new LanguageRule(_engine.Object, () => _settings);
    }

    private string? VoiceFor(string? language) =>
        _rule.ApplyTo(new Utterance("text", SpeechPriority.Normal) { Language = language }).Voice;

    [Fact]
    public void AnotherLanguage_UsesAVoiceForIt()
    {
        Assert.Equal("Microsoft Denise", VoiceFor("fr-FR"));
        Assert.Equal("Microsoft Sylvie", VoiceFor("fr-CA"));
        Assert.Equal("Microsoft Katja", VoiceFor("de"));
    }

    [Fact]
    public void AnotherRegion_FallsBackToTheLanguage()
    {
        Assert.Equal("Microsoft Katja", VoiceFor("de-AT"));
    }

    [Fact]
    public void TheCurrentVoicesLanguage_KeepsTheCurrentVoice_WhateverTheRegion()
    {
        Assert.Null(VoiceFor("en-GB"));
        Assert.Null(VoiceFor("en-US"));
    }

    [Fact]
    public void NoVoiceForTheLanguage_KeepsTheCurrentVoice()
    {
        Assert.Null(VoiceFor("ja-JP"));
    }

    [Fact]
    public void Untagged_IsUnchanged()
    {
        Assert.Null(VoiceFor(null));
    }

    [Fact]
    public void TheUsersMapping_Wins()
    {
        _settings = _settings with
        {
            LanguageVoices = new(StringComparer.OrdinalIgnoreCase) { ["fr"] = "Microsoft Sylvie" },
        };

        Assert.Equal("Microsoft Sylvie", VoiceFor("fr-FR"));
    }

    [Fact]
    public void AMappingToAVoiceThatIsntInstalled_IsIgnored()
    {
        _settings = _settings with
        {
            LanguageVoices = new(StringComparer.OrdinalIgnoreCase) { ["fr-FR"] = "Gone" },
        };

        Assert.Equal("Microsoft Denise", VoiceFor("fr-FR"));
    }

    [Fact]
    public void SwitchingOff_KeepsTheCurrentVoice()
    {
        _settings = _settings with { AutoLanguageSwitching = false };

        Assert.Null(VoiceFor("fr-FR"));
    }

    [Fact]
    public void TheVoiceList_IsNotReadForEveryUtterance()
    {
        VoiceFor("fr-FR");
        VoiceFor("de-DE");
        VoiceFor("fr-CA");

        _engine.Verify(e => e.GetVoiceDetails(), Times.Once);
    }
}
