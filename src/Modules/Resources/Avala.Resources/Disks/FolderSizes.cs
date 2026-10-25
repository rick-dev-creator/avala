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

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    private static readonly HashSet<string> BuildOutputs = new(StringComparer.Ordinal)
    {
        "node_modules", "bin", "obj", "target", "dist", "build", ".venv", "venv", "__pycache__", ".gradle", ".next", ".nuxt", ".turbo", ".cache",
    };

    public Task<long> SizeAsync(string folder, CancellationToken cancellationToken) => Task.Run(() => Measure(folder), cancellationToken);

    public Task<(long Bytes, long Build)> MeasureAsync(string folder, CancellationToken cancellationToken) => Task.Run(() => MeasureBuild(folder), cancellationToken);

    private static (long Bytes, long Build) MeasureBuild(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return (0, 0);
            }

            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            var files = new FileSystemEnumerable<(long Length, bool Build)>(
                root,
                (ref entry) => (entry.Length, InBuildOutput(root, entry.Directory.ToString())),
                Everything) { ShouldIncludePredicate = (ref entry) => !entry.IsDirectory };
            var (bytes, build) = (0L, 0L);

            foreach (var (length, inBuild) in files)
            {
                bytes += length;
                build += inBuild ? length : 0;
            }

            return (bytes, build);
        }
        catch (IOException)
        {
            return (0, 0);
        }
    }

    private static bool InBuildOutput(string root, string directory) =>
        directory.Length > root.Length
        && directory[(root.Length + 1)..].Split(Separators).Any(BuildOutputs.Contains);

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
