namespace Avala.Components.Pictures;

public interface IPictureDecoder
{
    Task<IPicture> DecodeAsync(byte[] encoded, int width, CancellationToken cancellationToken);
}
