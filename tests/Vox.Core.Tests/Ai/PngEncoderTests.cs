using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Vox.Core.Ai;
using Vox.Core.Ocr;
using Xunit;

namespace Vox.Core.Tests.Ai;

public class PngEncoderTests
{
    private static List<(string Type, byte[] Data)> Chunks(byte[] png)
    {
        var chunks = new List<(string, byte[])>();
        int at = 8;
        while (at < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = Encoding.ASCII.GetString(png, at + 4, 4);
            chunks.Add((type, png.AsSpan(at + 8, length).ToArray()));
            at += 12 + length;
        }
        return chunks;
    }

    [Fact]
    public void ThePicture_RoundTrips_AsRgb()
    {
        // Two pixels: blue and red, in BGRA
        var image = new CapturedImage(2, 1, [255, 0, 0, 255, 0, 0, 255, 255], new ScreenRect(0, 0, 2, 1));

        var png = PngEncoder.Encode(image);

        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
        var chunks = Chunks(png);
        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks.Select(c => c.Type));
        var header = chunks[0].Data;
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(header));
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)));
        Assert.Equal(2, header[9]); // RGB

        using var zlib = new ZLibStream(new MemoryStream(chunks[1].Data), CompressionMode.Decompress);
        using var rows = new MemoryStream();
        zlib.CopyTo(rows);
        Assert.Equal([0, 0, 0, 255, 255, 0, 0], rows.ToArray());
    }

    [Fact]
    public void EveryChunk_HasItsChecksum()
    {
        var png = PngEncoder.Encode(AiTestSupport.Image(7, 5));
        int at = 8;
        while (at < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            uint expected = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length));
            Assert.Equal(Crc32(png.AsSpan(at + 4, 4 + length)), expected);
            at += 12 + length;
        }
    }

    [Fact]
    public void ALargePicture_IsScaledDown()
    {
        var image = AiTestSupport.Image(400, 100);

        var png = PngEncoder.EncodeWithin(image, maxDimension: 200, maxBytes: 1_000_000);

        var header = Chunks(png)[0].Data;
        Assert.Equal(200, BinaryPrimitives.ReadInt32BigEndian(header));
        Assert.Equal(50, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)));
    }

    [Fact]
    public void ATooHeavyPicture_IsScaledUntilItFits()
    {
        // Noise: it compresses badly, as a photo does
        var pixels = new byte[300 * 300 * 4];
        new Random(1).NextBytes(pixels);
        var image = new CapturedImage(300, 300, pixels, new ScreenRect(0, 0, 300, 300));
        var full = PngEncoder.Encode(image).Length;

        var png = PngEncoder.EncodeWithin(image, maxDimension: 1000, maxBytes: full / 3);

        Assert.True(png.Length <= full / 3);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        return crc ^ 0xFFFFFFFF;
    }
}
