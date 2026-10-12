namespace Avala.Sdk;

public enum FileSaveError
{
    Cancelled,
    Unavailable,
    Unwritable,
}

public interface IFileSaver
{
    ValueTask<Result<string, FileSaveError>> SaveAsync(string title, string suggestedName, string content, CancellationToken cancellationToken);
}
