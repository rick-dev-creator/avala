namespace Avala.Sdk;

public interface IClipboard
{
    ValueTask<bool> CopyAsync(string text, CancellationToken cancellationToken);
}
