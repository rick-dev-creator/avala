using Avala.Components.Pictures;
using Avala.Components.UI.Pictures;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Avala.Components.UI.States;

public static class Pictures
{
    public static IValueConverter Shown { get; } = new FuncValueConverter<IPicture, Bitmap?>(picture => (picture as DecodedPicture)?.Bitmap);
}
