namespace Avala.Sdk;

public interface IFilePicker
{
    ValueTask<IReadOnlyList<string>> PickFilesAsync(string title, CancellationToken cancellationToken);
}
