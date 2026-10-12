using Avala.Components.UI.Pictures;
using Avala.Sdk;
using Avala.Testing.UI;

namespace Avala.Components.UI.Tests;

public sealed class PictureHeaderTests
{
    public static TheoryData<string, byte[], int> Headers => new()
    {
        { "png", Pngs.Of(1234, 5), 1234 },
        { "gif", [.. "GIF89a"u8, 0xD2, 0x04, 0x05, 0x00, 0x00], 1234 },
        { "jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x05, 0x04, 0xD2, 0x03, 0x00], 1234 },
        { "webp", [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, .. "VP8X"u8, 0, 0, 0, 0, 0, 0, 0, 0, 0xD1, 0x04, 0x00, 0x04, 0x00, 0x00], 1234 },
    };

    [Theory]
    [MemberData(nameof(Headers))]
    public void APicturesWidthIsReadFromItsHeader(string format, byte[] header, int width) =>
        Assert.Equal((format, Option<int>.Some(width)), (format, PictureHeader.Width(header)));

    [Fact]
    public void BytesThatAreNoPictureHaveNoWidth() =>
        Assert.True(PictureHeader.Width("plain text"u8).IsNone);
}
