namespace Avala.Components.Pictures;

public interface IPicture : IDisposable
{
    int Width { get; }

    bool IsNothing { get; }
}

public sealed class NoPicture : IPicture
{
    private NoPicture()
    {
    }

    public static NoPicture Instance { get; } = new();

    public int Width => 0;

    public bool IsNothing => true;

    public void Dispose()
    {
    }
}
