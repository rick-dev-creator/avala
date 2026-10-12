using System.Buffers.Binary;
using System.Text;

namespace Avala.Testing.UI;

public static class Pngs
{
    public static byte[] Of(int width, int height, byte shade = 0x80)
    {
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", Stored(Pixels(width, height, shade)));
        Chunk(png, "IEND", []);

        return [.. png];
    }

    private static byte[] Pixels(int width, int height, byte shade)
    {
        var pixels = new byte[(width + 1) * height];

        for (var y = 0; y < height; y++)
        {
            Array.Fill(pixels, shade, (y * (width + 1)) + 1, width);
        }

        return pixels;
    }

    private static byte[] Stored(byte[] data)
    {
        var zlib = new List<byte> { 0x78, 0x01 };

        for (var at = 0; at < data.Length || at == 0; at += ushort.MaxValue)
        {
            var length = Math.Min(ushort.MaxValue, data.Length - at);
            zlib.Add(at + length >= data.Length ? (byte)1 : (byte)0);
            zlib.AddRange([(byte)length, (byte)(length >> 8), (byte)~length, (byte)(~length >> 8)]);
            zlib.AddRange(data.AsSpan(at, length));
        }

        uint low = 1;
        uint high = 0;

        foreach (var value in data)
        {
            low = (low + value) % 65521;
            high = (high + low) % 65521;
        }

        var adler = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(adler, (high << 16) | low);
        zlib.AddRange(adler);

        return [.. zlib];
    }

    private static void Chunk(List<byte> png, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.AddRange(length);
        var named = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        png.AddRange(named);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(named));
        png.AddRange(crc);
    }

    private static uint Crc(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var value in bytes)
        {
            crc ^= value;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }
}
