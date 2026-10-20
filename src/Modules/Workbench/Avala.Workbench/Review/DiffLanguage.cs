namespace Avala.Workbench.Review;

internal static class DiffLanguage
{
    public static string Of(string path)
    {
        var name = Path.GetFileName(path.Replace('\\', '/'));
        var extension = Path.GetExtension(name);

        return extension.Length > 1 ? extension[1..] : name;
    }

    public static string Text(IEnumerable<HunkLine> lines) => string.Join('\n', lines.Select(line => line.Text));
}
