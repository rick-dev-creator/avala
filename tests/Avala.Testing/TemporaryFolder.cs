using Avala.Sdk;

namespace Avala.Testing;

public sealed class TemporaryFolder : IDisposable, IAsyncDisposable
{
    private const int Attempts = 50;

    private const int RemainingShown = 20;

    private static readonly TimeSpan Settling = TimeSpan.FromMilliseconds(100);

    private static readonly EnumerationOptions OwnFiles = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private readonly DirectoryInfo folder = Create();

    public string Path => folder.FullName;

    internal static DirectoryInfo Create() => new(Directory.CreateTempSubdirectory("avala-").FullName.Canonical());

    public void Dispose() => RemoveExisting(folder);

    public ValueTask DisposeAsync() => RemoveAsync(folder);

    internal static async ValueTask RemoveAsync(DirectoryInfo folder)
    {
        for (var attempt = 1; attempt < Attempts; attempt++)
        {
            try
            {
                RemoveExisting(folder);

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(Settling);
            }
        }

        RemoveExisting(folder);
    }

    private static void RemoveExisting(DirectoryInfo folder)
    {
        folder.Refresh();

        if (folder.Exists)
        {
            Remove(folder);
        }
    }

    private static void Remove(DirectoryInfo folder)
    {
        foreach (var file in folder.EnumerateFiles("*", OwnFiles))
        {
            file.Attributes = FileAttributes.Normal;
        }

        try
        {
            folder.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"{folder.FullName} could not be removed: {exception.Message} What remains, of which something is still held open or is a process's working directory: {Remaining(folder)}", exception);
        }
    }

    private static string Remaining(DirectoryInfo folder)
    {
        folder.Refresh();

        return !folder.Exists
            ? "nothing"
            : string.Join(", ", folder.EnumerateFileSystemInfos("*", OwnFiles).Select(entry => entry.FullName).Prepend(folder.FullName).Take(RemainingShown));
    }
}
