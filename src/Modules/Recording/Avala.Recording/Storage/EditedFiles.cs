using Avala.Recording.Capturing;
using Avala.Sdk;

namespace Avala.Recording.Storage;

internal sealed class EditedFiles : IEditedFiles
{
    public const int MaximumBytes = 1024 * 1024;

    private static readonly EnumerationOptions Everything = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public Task<FolderStamps> StampAsync(string folder, CancellationToken cancellationToken) => Task.Run(() => Stamp(folder), cancellationToken);

    private static FolderStamps Stamp(string folder)
    {
        try
        {
            return new FolderStamps(Directory.Exists(folder)
                ? new DirectoryInfo(folder).EnumerateFiles("*", Everything)
                    .Where(file => !Path.GetRelativePath(folder, file.FullName).Split(Path.DirectorySeparatorChar).Contains(".git"))
                    .ToDictionary(file => file.FullName, file => new FileStamp(file.Length, file.LastWriteTimeUtc), StringComparer.Ordinal)
                : new Dictionary<string, FileStamp>(StringComparer.Ordinal));
        }
        catch (IOException)
        {
            return new FolderStamps(new Dictionary<string, FileStamp>(StringComparer.Ordinal));
        }
    }

    public async ValueTask<Option<string>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);

            if (stream.Length > MaximumBytes)
            {
                return Option<string>.None;
            }

            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Option<string>.None;
        }
    }
}
