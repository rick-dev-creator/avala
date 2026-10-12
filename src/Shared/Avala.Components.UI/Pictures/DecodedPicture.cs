using Avala.Components.Pictures;
using Avalonia.Media.Imaging;

namespace Avala.Components.UI.Pictures;

public sealed class DecodedPicture(Bitmap bitmap, Action release) : IPicture
{
    private bool released;

    public Bitmap Bitmap => bitmap;

    public int Width => bitmap.PixelSize.Width;

    public bool IsNothing => false;

    public void Dispose()
    {
        if (!released)
        {
            released = true;
            release();
        }
    }
}
