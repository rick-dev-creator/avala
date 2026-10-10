namespace Avala.Testing;

public sealed class TemporaryFolder : IDisposable, IAsyncDisposable
{
    private const int Attempts = 50;

    private static readonly TimeSpan Settling = TimeSpan.FromMilliseconds(100);

    private static readonly EnumerationOptions OwnFiles = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("avala-");

    public string Path => folder.FullName;

    public void Dispose()
    {
        folder.Refresh();

        if (folder.Exists)
        {
            Remove(folder);
        }
    }

    internal static void Remove(DirectoryInfo folder)
    {
        foreach (var file in folder.EnumerateFiles("*", OwnFiles))
        {
            file.Attributes = FileAttributes.Normal;
        }

        folder.Delete(recursive: true);
    }

    public async ValueTask DisposeAsync()
    {
        for (var attempt = 1; attempt < Attempts; attempt++)
        {
            try
            {
                Dispose();

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(Settling);
            }
        }

        Dispose();
    }
}
