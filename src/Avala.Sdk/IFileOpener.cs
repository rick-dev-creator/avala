namespace Avala.Sdk;

public enum FileOpenError
{
    NotFound,
    Unavailable,
    Refused,
}

public interface IFileOpener
{
    ValueTask<Result<string, FileOpenError>> OpenAsync(string path, CancellationToken cancellationToken);
}
