namespace Avala.Sdk;

public interface IClipboard
{
    ValueTask<bool> CopyAsync(string text, CancellationToken cancellationToken);

    ValueTask<bool> CopyImageAsync(string path, CancellationToken cancellationToken);

    ValueTask<bool> CopyFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken);

    ValueTask<bool> CopyLinkAsync(Uri link, CancellationToken cancellationToken);
}
