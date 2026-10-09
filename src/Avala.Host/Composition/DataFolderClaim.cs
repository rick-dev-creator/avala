using Avala.Sdk;

namespace Avala.Host.Composition;

internal sealed class DataFolderClaim : IDisposable
{
    public const string Marker = "avala.lock";

    private readonly FileStream handle;

    private DataFolderClaim(FileStream handle) => this.handle = handle;

    public static Option<DataFolderClaim> TryTake(AvalaPaths paths)
    {
        Directory.CreateDirectory(paths.Data);

        try
        {
            return new DataFolderClaim(new FileStream(Path.Combine(paths.Data, Marker), new FileStreamOptions
            {
                Mode = FileMode.OpenOrCreate,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            }));
        }
        catch (IOException)
        {
            return Option<DataFolderClaim>.None;
        }
    }

    public void Dispose() => handle.Dispose();
}
