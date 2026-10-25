using Avala.Permissions.Answering;
using Avala.Sdk;

namespace Avala.Permissions.Links;

internal sealed class SymbolicLinks : IRealPaths
{
    private const int MostLinks = 40;

    private const int MostMatches = 4096;

    private const int MostEntries = 65536;

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    private static readonly char[] Wildcards = ['*', '?'];

    private static readonly EnumerationOptions Listing = new() { AttributesToSkip = 0, IgnoreInaccessible = false, ReturnSpecialDirectories = false, RecurseSubdirectories = false };

    public Option<IReadOnlyList<string>> Expand(string pattern)
    {
        var root = Path.GetPathRoot(pattern) ?? string.Empty;
        IReadOnlyList<string> found = [root];
        var visited = 0;

        foreach (var part in pattern[root.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = new List<string>();

            foreach (var folder in found)
            {
                if (part.IndexOfAny(Wildcards) < 0)
                {
                    next.Add(Path.Combine(folder, part));
                }
                else if (!Listed(folder, part, next, ref visited))
                {
                    return Option<IReadOnlyList<string>>.None;
                }
            }

            found = next;
        }

        return Option<IReadOnlyList<string>>.Some(found);
    }

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

    public Option<string> Repository(string path)
    {
        for (var folder = Path.GetDirectoryName(path); !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder))
        {
            var git = Path.Combine(folder, ".git");

            if (Directory.Exists(git) || File.Exists(git))
            {
                return folder;
            }
        }

        return Option<string>.None;
    }

    private static bool Listed(string folder, string part, List<string> matches, ref int visited)
    {
        if (!Directory.Exists(folder))
        {
            return true;
        }

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder, "*", Listing))
            {
                if (++visited > MostEntries || entry.Contains('\uFFFD', StringComparison.Ordinal))
                {
                    return false;
                }

                if (Fits(part, Path.GetFileName(entry)))
                {
                    matches.Add(entry);
                }

                if (matches.Count > MostMatches)
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool Fits(string part, string name)
    {
        var (p, n, star, resume) = (0, 0, -1, 0);

        while (n < name.Length)
        {
            if (p < part.Length && part[p] is '*' or '?')
            {
                (star, resume, p) = (p, n, p + 1);
            }
            else if (p < part.Length && Same(part[p], name[n]))
            {
                (p, n) = (p + 1, n + 1);
            }
            else if (star >= 0)
            {
                (p, resume) = (star + 1, resume + 1);
                n = resume;
            }
            else
            {
                return false;
            }
        }

        return part[p..].All(character => character is '*' or '?');
    }

    private static bool Same(char wanted, char named) => !char.IsAscii(named) || char.ToUpperInvariant(wanted) == char.ToUpperInvariant(named);

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
