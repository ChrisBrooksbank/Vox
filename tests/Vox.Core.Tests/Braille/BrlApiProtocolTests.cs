using Vox.Core.Braille.Brltty;
using Xunit;

namespace Vox.Core.Tests.Braille;

public class BrlApiProtocolTests
{
    [Fact]
    public void Encode_WritesBigEndianSizeTypeAndPayload()
    {
        var packet = BrlApiProtocol.EncodeVersion();

        Assert.Equal(new byte[] { 0, 0, 0, 4, 0, 0, 0, (byte)'v', 0, 0, 0, 8 }, packet);
    }

    [Fact]
    public void ReadPacket_ReadsFramedPacketsAndNullAtEnd()
    {
        var bytes = BrlApiProtocol.EncodeVersion().Concat(BrlApiProtocol.EncodeEmpty(BrlApiPacketType.Ack)).ToArray();
        using var stream = new MemoryStream(bytes);

        var first = BrlApiProtocol.ReadPacket(stream);
        var second = BrlApiProtocol.ReadPacket(stream);

        Assert.Equal(BrlApiPacketType.Version, first!.Type);
        Assert.Equal(8u, BrlApiProtocol.DecodeVersion(first.Payload));
        Assert.Equal(BrlApiPacketType.Ack, second!.Type);
        Assert.Empty(second.Payload);
        Assert.Null(BrlApiProtocol.ReadPacket(stream));
    }

    [Fact]
    public void ReadPacket_ThrowsWhenTheStreamEndsInsideAPacket()
    {
        var bytes = BrlApiProtocol.EncodeVersion()[..10];
        using var stream = new MemoryStream(bytes);

        Assert.Throws<EndOfStreamException>(() => BrlApiProtocol.ReadPacket(stream));
    }

    [Fact]
    public void DecodeHeader_RejectsOversizedPackets()
    {
        var header = new byte[] { 0, 0, 0x20, 0, 0, 0, 0, (byte)'w' };

        Assert.Throws<BrlApiProtocolException>(() => BrlApiProtocol.DecodeHeader(header));
    }

    [Fact]
    public void EncodeAuthKey_SendsKeyTypeThenKeyBytes()
    {
        var packet = BrlApiProtocol.EncodeAuthKey(new byte[] { 1, 2, 3 });

        Assert.Equal(new byte[] { 0, 0, 0, 7, 0, 0, 0, (byte)'a', 0, 0, 0, (byte)'K', 1, 2, 3 }, packet);
    }

    [Fact]
    public void DecodeAuthMethods_ReadsEachMethod()
    {
        var payload = new byte[] { 0, 0, 0, (byte)'K', 0, 0, 0, (byte)'N' };

        Assert.Equal(new[] { BrlApiProtocol.AuthKey, BrlApiProtocol.AuthNone }, BrlApiProtocol.DecodeAuthMethods(payload));
    }

    [Fact]
    public void EncodeEnterTtyMode_WithNoPathAndNoDriver()
    {
        var packet = BrlApiProtocol.EncodeEnterTtyMode([]);

        Assert.Equal(new byte[] { 0, 0, 0, 5, 0, 0, 0, (byte)'t', 0, 0, 0, 0, 0 }, packet);
    }

    [Fact]
    public void EncodeEnterTtyMode_WithPathAndDriver()
    {
        var packet = BrlApiProtocol.EncodeEnterTtyMode([7, -1], "bm");

        Assert.Equal(
            new byte[] { 0, 0, 0, 15, 0, 0, 0, (byte)'t', 0, 0, 0, 2, 0, 0, 0, 7, 0xFF, 0xFF, 0xFF, 0xFF, 2, (byte)'b', (byte)'m' },
            packet);
    }

    [Fact]
    public void EncodeWriteDots_SendsRegionBlankTextMasksAndCursorOff()
    {
        var packet = BrlApiProtocol.EncodeWriteDots(3, new byte[] { 0x13, 0x01 });

        var expected = new byte[]
        {
            0, 0, 0, 26, 0, 0, 0, (byte)'w',
            0, 0, 0, 0x3E,          // flags: region | text | and | or | cursor
            0, 0, 0, 3, 0, 0, 0, 2, // region: begin 3 (1-based), size 2
            0, 0, 0, 2, 0x20, 0x20, // text: two blanks
            0, 0,                   // AND mask
            0x13, 0x01,             // OR mask: the dots
            0, 0, 0, 0,             // cursor off
        };
        Assert.Equal(expected, packet);
    }

    [Fact]
    public void DecodeDisplaySizeKeyAndException()
    {
        Assert.Equal((40, 1), BrlApiProtocol.DecodeDisplaySize(new byte[] { 0, 0, 0, 40, 0, 0, 0, 1 }));
        Assert.Equal(0x20010005UL | (1UL << 32),
            BrlApiProtocol.DecodeKey(new byte[] { 0, 0, 0, 1, 0x20, 0x01, 0, 5 }));
        Assert.Equal((17u, BrlApiPacketType.Write),
            BrlApiProtocol.DecodeException(new byte[] { 0, 0, 0, 17, 0, 0, 0, (byte)'w' }));
        Assert.Equal("authentication failed", BrlApiProtocol.ErrorName(17));
        Assert.Equal("bm", BrlApiProtocol.DecodeString("bm\0"u8));
    }

    [Theory]
    [InlineData(null, "127.0.0.1", 4101)]
    [InlineData("", "127.0.0.1", 4101)]
    [InlineData(":0", "127.0.0.1", 4101)]
    [InlineData(":2", "127.0.0.1", 4103)]
    [InlineData("localhost:1", "127.0.0.1", 4102)]
    [InlineData("braille-pc", "braille-pc", 4101)]
    [InlineData("10.0.0.5:3", "10.0.0.5", 4104)]
    public void ParseHost_ReadsBrlApiHostStrings(string? spec, string host, int port)
    {
        Assert.Equal((host, port), BrlApiProtocol.ParseHost(spec));
    }

    [Fact]
    public void ParseHost_RejectsABadDisplayNumber()
    {
        Assert.Throws<FormatException>(() => BrlApiProtocol.ParseHost("host:x"));
    }
}
