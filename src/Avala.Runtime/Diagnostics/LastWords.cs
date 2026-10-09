namespace Avala.Runtime.Diagnostics;

internal static class LastWords
{
    public static void Append(string path, byte[] line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            file.Write(line);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
        }
    }
}
