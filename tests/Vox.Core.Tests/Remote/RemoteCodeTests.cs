using Vox.Core.Remote;
using Xunit;

namespace Vox.Core.Tests.Remote;

public class RemoteCodeTests
{
    [Fact]
    public void ANewCode_IsTwelveCharacters_InThreeGroups()
    {
        var code = RemoteCode.New();

        Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$", code);
        Assert.NotNull(RemoteCode.Normalize(code));
        Assert.NotEqual(code, RemoteCode.New());
    }

    [Theory]
    [InlineData("abcd-efgh-jkmn", "ABCDEFGHJKMN")]
    [InlineData(" ABCD EFGH JKMN ", "ABCDEFGHJKMN")]
    [InlineData("O0IL-1234-5678", "0011" + "12345678")]
    public void TypedCodes_AreNormalized(string typed, string expected) =>
        Assert.Equal(expected, RemoteCode.Normalize(typed));

    [Theory]
    [InlineData("")]
    [InlineData("ABCD-EFGH")]
    [InlineData("ABCD-EFGH-JKMNP")]
    [InlineData("ABCD-EFGH-JKM!")]
    [InlineData("ABCD-EFGH-JKMU")]
    public void NonCodes_AreNot(string typed) => Assert.Null(RemoteCode.Normalize(typed));

    [Fact]
    public void ACode_IsSpelledOut()
    {
        Assert.Equal("A B C D, E F G H, J K M N", RemoteCode.Spoken("abcdefghjkmn"));
        Assert.Equal("ABCD-EFGH-JKMN", RemoteCode.Format("abcdefghjkmn"));
    }

    [Fact]
    public void TheRelayChannel_IsTheSameForTheSameCode_AndDoesntShowIt()
    {
        var channel = RemoteCode.RelayChannel("ABCD-EFGH-JKMN");

        Assert.Equal(channel, RemoteCode.RelayChannel("abcdefghjkmn"));
        Assert.NotEqual(channel, RemoteCode.RelayChannel("ABCD-EFGH-JKMP"));
        Assert.Matches("^[0-9a-f]{32}$", channel);
        Assert.DoesNotContain("abcd", channel);
    }
}
