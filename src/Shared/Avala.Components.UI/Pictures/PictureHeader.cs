using System.Buffers.Binary;
using Avala.Sdk;

namespace Avala.Components.UI.Pictures;

public static class PictureHeader
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static Option<int> Width(ReadOnlySpan<byte> bytes) =>
        IsPng(bytes) ? Positive(BinaryPrimitives.ReadInt32BigEndian(bytes[16..]))
        : IsGif(bytes) ? Positive(BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]))
        : IsWebP(bytes) ? WebP(bytes)
        : IsJpeg(bytes) ? Jpeg(bytes)
        : Option<int>.None;

    private static bool IsPng(ReadOnlySpan<byte> bytes) => bytes.StartsWith(PngSignature) && bytes.Length >= 24;

    private static bool IsGif(ReadOnlySpan<byte> bytes) => bytes.StartsWith("GIF8"u8) && bytes.Length >= 10;

    private static bool IsWebP(ReadOnlySpan<byte> bytes) => bytes.StartsWith("RIFF"u8) && bytes.Length >= 30 && bytes[8..12].SequenceEqual("WEBP"u8);

    private static bool IsJpeg(ReadOnlySpan<byte> bytes) => bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8;

    private static Option<int> WebP(ReadOnlySpan<byte> bytes) =>
        bytes[12..16].SequenceEqual("VP8 "u8) ? Positive(BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..]) & 0x3FFF)
        : bytes[12..16].SequenceEqual("VP8L"u8) ? Positive(1 + (((bytes[22] & 0x3F) << 8) | bytes[21]))
        : bytes[12..16].SequenceEqual("VP8X"u8) ? Positive(1 + (bytes[24] | (bytes[25] << 8) | (bytes[26] << 16)))
        : Option<int>.None;

    private static Option<int> Jpeg(ReadOnlySpan<byte> bytes)
    {
        var at = 2;

        while (at + 9 < bytes.Length && bytes[at] == 0xFF)
        {
            var marker = bytes[at + 1];
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 2)..]);

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return Positive(BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 7)..]));
            }

            at += 2 + length;
        }

        return Option<int>.None;
    }

    private static Option<int> Positive(int width) => width > 0 ? width : Option<int>.None;
}
