namespace Avala.Testing;

public sealed class TemporaryFolder : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("avala-");

    public string Path => folder.FullName;

    public void Dispose()
    {
        foreach (var file in folder.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        folder.Delete(recursive: true);
    }
}
