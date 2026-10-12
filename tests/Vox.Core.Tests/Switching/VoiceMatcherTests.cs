using Vox.Core.Switching;
using Xunit;

namespace Vox.Core.Tests.Switching;

public sealed class VoiceMatcherTests
{
    private static readonly string[] OneCore = ["Microsoft David", "Microsoft Hazel", "Microsoft Zira"];
    private static readonly string[] Sapi = ["Microsoft David Desktop", "Microsoft Zira Desktop"];

    [Theory]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices\Tokens\MSTTS_V110_enGB_HazelM", "Microsoft Hazel")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices\Tokens\MSTTS_V110_enUS_ZiraM", "Microsoft Zira")]
    [InlineData("Microsoft David", "Microsoft David")]
    [InlineData("microsoft zira", "Microsoft Zira")]
    public void Match_FindsOneCoreVoices(string id, string expected) =>
        Assert.Equal(expected, VoiceMatcher.Match(id, OneCore));

    [Fact]
    public void Match_FindsSapiVoicesByTheirToken() =>
        Assert.Equal("Microsoft Zira Desktop",
            VoiceMatcher.Match(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices\Tokens\TTS_MS_EN-US_ZIRA_11.0", Sapi));

    [Theory]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices\Tokens\MSTTS_V110_frFR_HortenseM")]
    [InlineData("")]
    [InlineData(@"en\en-us")]
    public void Match_IsNullWhenNoInstalledVoiceIsTheOne(string id) =>
        Assert.Null(VoiceMatcher.Match(id, OneCore));
}
