namespace Avala.Triggers.Catalog;

internal static class Repositories
{
    public static string Key(string repository) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository));
}
