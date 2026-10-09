using Avala.Resources.Tracking;
using Avala.Sdk;

namespace Avala.Resources.Disks;

internal sealed class FolderSizes(AvalaPaths paths) : IFolderSizes
{
    public Task<long> DataFolderAsync(CancellationToken cancellationToken) => SizeAsync(paths.Data, cancellationToken);

    private static readonly EnumerationOptions Everything = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public Task<long> SizeAsync(string folder, CancellationToken cancellationToken) => Task.Run(() => Measure(folder), cancellationToken);

    private static long Measure(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? new DirectoryInfo(folder).EnumerateFiles("*", Everything).Sum(file => file.Length) : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
