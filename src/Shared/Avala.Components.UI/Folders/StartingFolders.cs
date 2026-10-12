namespace Avala.Components.UI.Folders;

internal static class StartingFolders
{
    public static string Near(string typed, string home) =>
        Expanded(typed.Trim(), home) is { Length: > 0 } expanded && Path.IsPathFullyQualified(expanded)
            ? Existing(expanded).FirstOrDefault(Directory.Exists) ?? home
            : home;

    private static string Expanded(string typed, string home) =>
        typed == "~" || typed.StartsWith("~/", StringComparison.Ordinal) ? Path.Join(home, typed[1..]) : typed;

    private static IEnumerable<string> Existing(string folder)
    {
        for (var current = folder; current is not null; current = Path.GetDirectoryName(current))
        {
            yield return current;
        }
    }
}
