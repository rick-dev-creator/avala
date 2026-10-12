using Avala.Components.Pictures;

namespace Avala.Workbench.Tests;

internal sealed class FakePictures : IPictureDecoder
{
    public List<FakePicture> Decoded { get; } = [];

    public IEnumerable<int> Held => Decoded.Where(picture => !picture.IsReleased).Select(picture => picture.Width);

    public Task<IPicture> DecodeAsync(byte[] encoded, int width, CancellationToken cancellationToken)
    {
        var picture = new FakePicture(width);
        Decoded.Add(picture);

        return Task.FromResult<IPicture>(picture);
    }
}

internal sealed class FakePicture(int width) : IPicture
{
    public int Width => width;

    public bool IsNothing => false;

    public bool IsReleased { get; private set; }

    public void Dispose() => IsReleased = true;
}
