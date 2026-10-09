namespace Avala.Testing;

public static class HostPaths
{
    private static readonly string Root = Path.GetPathRoot(Path.GetTempPath())!;

    public static string Rooted(string path) =>
        path.StartsWith('/') ? Path.Combine(Root, path[1..].Replace('/', Path.DirectorySeparatorChar)) : path;
}
