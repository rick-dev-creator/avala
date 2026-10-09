namespace Avala.Sdk;

public enum FileOpenError
{
    Uncreatable,
    Unavailable,
    Refused,
}

public sealed record OpenedFile(string Path, bool Created);

public interface IFileOpener
{
    ValueTask<Result<OpenedFile, FileOpenError>> OpenAsync(string path, string template, CancellationToken cancellationToken);

    ValueTask<Result<OpenedFile, FileOpenError>> OpenFolderAsync(string path, CancellationToken cancellationToken);
}
