using Avala.Permissions.Answering;
using Avala.Sdk;

namespace Avala.Permissions.Links;

internal sealed class SymbolicLinks : IRealPaths
{
    private const int MostLinks = 40;

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public Option<string> Resolve(string path)
    {
        var current = Path.GetPathRoot(path) ?? string.Empty;
        var pending = new List<string>(path[current.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries));
        var followed = 0;

        while (pending.Count > 0)
        {
            var part = pending[0];
            pending.RemoveAt(0);

            if (part == "..")
            {
                current = Path.GetDirectoryName(current) ?? current;
            }
            else if (part != "." && Target(Path.Combine(current, part)) is { Length: > 0 } link)
            {
                if (++followed > MostLinks)
                {
                    return Option<string>.None;
                }

                var target = Path.Combine(current, link);
                current = Path.GetPathRoot(target) ?? string.Empty;
                pending.InsertRange(0, target[current.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries));
            }
            else if (part != ".")
            {
                current = Path.Combine(current, part);
            }
        }

        return current;
    }

    private static string Target(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget ?? new DirectoryInfo(path).LinkTarget ?? string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return string.Empty;
        }
    }
}
