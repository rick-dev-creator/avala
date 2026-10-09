using System.Text;

namespace Avala.Sdk;

public static class PortablePaths
{
    public const char Separator = '/';

    private const string PathEnds = "\"'`<>|*?()[]{},;";

    extension(string text)
    {
        public string Marking(string folder, string mark) => Rewritten(text, folder, mark, Separator);

        public string Unmarking(string mark, string folder, char separator) => Rewritten(text, mark, folder, separator);

        public string Canonical()
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(text));
            var root = Path.GetPathRoot(full) ?? string.Empty;
            var parts = full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            var resolved = root;

            for (var index = 0; index < parts.Length; index++)
            {
                var next = Path.Combine(resolved, parts[index]);

                if (!Path.Exists(next))
                {
                    return Path.Combine([next, .. parts[(index + 1)..]]);
                }

                resolved = LinkTarget(next).Match(target => target.Canonical(), () => next);
            }

            return resolved;
        }
    }

    private static Option<string> LinkTarget(string path)
    {
        try
        {
            FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);

            return (entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName).ToOption();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Option<string>.None;
        }
    }

    private static string Rewritten(string text, string from, string to, char separator)
    {
        if (from.Length == 0)
        {
            return text;
        }

        var written = new StringBuilder(text.Length);
        var position = 0;

        for (var found = text.IndexOf(from, StringComparison.Ordinal); found >= 0; found = text.IndexOf(from, position, StringComparison.Ordinal))
        {
            written.Append(text, position, found - position).Append(to);
            position = found + from.Length;

            for (; position < text.Length && !char.IsWhiteSpace(text[position]) && !PathEnds.Contains(text[position], StringComparison.Ordinal); position++)
            {
                written.Append(text[position] is '/' or '\\' ? separator : text[position]);
            }
        }

        return written.Append(text, position, text.Length - position).ToString();
    }
}
