using Avala.Resources.Contracts;

namespace Avala.Resources.Tracking;

internal interface IResourceSettings
{
    ValueTask<ResourceSettings> LoadAsync(CancellationToken cancellationToken);
}

internal interface IFolderSizes
{
    Task<long> SizeAsync(string folder, CancellationToken cancellationToken);

    Task<long> DataFolderAsync(CancellationToken cancellationToken);
}

internal static class Folders
{
    public static string Key(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
}
