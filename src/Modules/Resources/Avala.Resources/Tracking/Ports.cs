using Avala.Resources.Contracts;
using Avala.Sdk;

namespace Avala.Resources.Tracking;

internal interface IResourceSettings
{
    ValueTask<ResourceSettings> LoadAsync(CancellationToken cancellationToken);

    ValueTask<Result<ResourceSettings, ResourceError>> ChangeWorktreesAsync(WorktreePolicy policy, CancellationToken cancellationToken);
}

internal interface IFolderSizes
{
    Task<long> SizeAsync(string folder, CancellationToken cancellationToken);

    Task<long> DataFolderAsync(CancellationToken cancellationToken);

    Option<DiskSpace> Disk();
}

internal static class Folders
{
    public static string Key(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
}
