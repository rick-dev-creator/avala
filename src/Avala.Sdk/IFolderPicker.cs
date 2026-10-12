namespace Avala.Sdk;

public interface IFolderPicker
{
    ValueTask<Option<string>> PickAsync(string title, string near, CancellationToken cancellationToken);
}
