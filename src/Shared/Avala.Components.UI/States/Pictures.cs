using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Avala.Components.UI.States;

public static class Pictures
{
    public static IValueConverter Decoded { get; } = new FuncValueConverter<byte[], Bitmap?>(Decode);

    private static Bitmap? Decode(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);

            return new Bitmap(stream);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            return null;
        }
    }
}
