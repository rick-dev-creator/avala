using System.IO.Enumeration;
using Avala.Resources.Contracts;
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

    public Option<DiskSpace> Disk()
    {
        try
        {
            var drive = new DriveInfo(Path.GetFullPath(paths.Data));

            return drive.IsReady ? new DiskSpace(drive.AvailableFreeSpace, drive.TotalSize) : Option<DiskSpace>.None;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return Option<DiskSpace>.None;
        }
    }

    private static long Measure(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? new FileSystemEnumerable<long>(folder, (ref entry) => entry.Length, Everything) { ShouldIncludePredicate = (ref entry) => !entry.IsDirectory }.Sum()
                : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
