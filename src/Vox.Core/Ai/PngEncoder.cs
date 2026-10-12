using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Vox.Core.Ocr;

namespace Vox.Core.Ai;

/// <summary>
/// Encodes a <see cref="CapturedImage"/> as a PNG (8-bit RGB, no filtering), the format pictures
/// are sent to describers in. Small enough to need nothing from Windows.
/// </summary>
public static class PngEncoder
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(CapturedImage image)
    {
        using var output = new MemoryStream();
        output.Write(Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = 8; // bits per channel
        header[9] = 2; // RGB: the screen has no transparency worth sending
        WriteChunk(output, "IHDR", header);

        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                var row = new byte[1 + image.Width * 3];
                var pixels = image.Pixels;
                for (int y = 0; y < image.Height; y++)
                {
                    row[0] = 0; // no filter
                    int source = y * image.Width * 4;
                    for (int x = 0, target = 1; x < image.Width; x++, source += 4, target += 3)
                    {
                        // BGRA to RGB
                        row[target] = pixels[source + 2];
                        row[target + 1] = pixels[source + 1];
                        row[target + 2] = pixels[source];
                    }
                    zlib.Write(row);
                }
            }
            WriteChunk(output, "IDAT", compressed.ToArray());
        }

        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc(uint crc, byte[] data)
    {
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    /// <summary>
    /// <paramref name="image"/> as a PNG no larger than <paramref name="maxDimension"/> on its long
    /// side and <paramref name="maxBytes"/> in all (scaled down until it fits).
    /// </summary>
    public static byte[] EncodeWithin(CapturedImage image, int maxDimension, int maxBytes)
    {
        int longest = Math.Max(image.Width, image.Height);
        var scaled = longest > maxDimension ? image.Scaled(maxDimension / (double)longest) : image;
        var png = Encode(scaled);
        while (png.Length > maxBytes && Math.Max(scaled.Width, scaled.Height) > 64)
        {
            scaled = scaled.Scaled(0.75);
            png = Encode(scaled);
        }
        return png;
    }
}
