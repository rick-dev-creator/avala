using Avala.Permissions.Answering;
using Avala.Sdk;

namespace Avala.Permissions.Links;

internal sealed class SymbolicLinks : IRealPaths
{
    private const int MostLinks = 40;

    private const int MostMatches = 4096;

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    private static readonly char[] Wildcards = ['*', '?', '['];

    private static readonly EnumerationOptions Listing = new() { AttributesToSkip = 0, IgnoreInaccessible = true, ReturnSpecialDirectories = false, RecurseSubdirectories = false };

    public Option<IReadOnlyList<string>> Expand(string pattern)
    {
        var root = Path.GetPathRoot(pattern) ?? string.Empty;
        IReadOnlyList<string> found = [root];

        foreach (var part in pattern[root.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            found = part.IndexOfAny(Wildcards) >= 0 ? [.. found.SelectMany(folder => Entries(folder, part)).Take(MostMatches + 1)] : [.. found.Select(folder => Path.Combine(folder, part))];

            if (found.Count > MostMatches)
            {
                return Option<IReadOnlyList<string>>.None;
            }
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

    private static IEnumerable<string> Entries(string folder, string part)
    {
        try
        {
            return Directory.Exists(folder) ? [.. Directory.EnumerateFileSystemEntries(folder, "*", Listing).Where(entry => Fits(part, Path.GetFileName(entry))).Take(MostMatches + 1)] : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static bool Fits(string part, string name)
    {
        if (part.Contains('[', StringComparison.Ordinal))
        {
            return true;
        }

        var (p, n, star, resume) = (0, 0, -1, 0);

        while (n < name.Length)
        {
            if (p < part.Length && (part[p] == '?' || char.ToUpperInvariant(part[p]) == char.ToUpperInvariant(name[n])))
            {
                (p, n) = (p + 1, n + 1);
            }
            else if (p < part.Length && part[p] == '*')
            {
                (star, resume, p) = (p, n, p + 1);
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

        return part[p..].All(character => character == '*');
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
