using System.Text;
using System.Text.RegularExpressions;

namespace Avala.Workspaces.Testing;

internal sealed class GitIgnore(IReadOnlyList<GitIgnore.Rule> rules)
{
    private const string FileName = ".gitignore";

    public static GitIgnore None { get; } = new([]);

    public static async Task<GitIgnore> ReadAsync(string folder, CancellationToken cancellationToken)
    {
        var rules = new List<Rule>();

        foreach (var file in Directory.EnumerateFiles(folder, FileName, new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .OrderBy(found => Path.GetRelativePath(folder, found).Count(letter => letter is '/' or '\\'))
            .ThenBy(found => found, StringComparer.Ordinal))
        {
            var directory = FolderSnapshot.Normal(Path.GetRelativePath(folder, Path.GetDirectoryName(file)!));
            var under = directory == "." ? string.Empty : directory + "/";

            if (under.StartsWith(".git/", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var line in (await File.ReadAllTextAsync(file, cancellationToken)).Split('\n'))
            {
                rules.AddRange(Parse(under, line.TrimEnd('\r')));
            }
        }

        return new GitIgnore(rules);
    }

    public bool Ignores(string path)
    {
        var parts = path.Split('/');

        for (var depth = 1; depth < parts.Length; depth++)
        {
            if (Matches(string.Join('/', parts[..depth]), directory: true))
            {
                return true;
            }
        }

        return Matches(path, directory: false);
    }

    private bool Matches(string path, bool directory) =>
        rules.LastOrDefault(rule => path.StartsWith(rule.Under, StringComparison.Ordinal) && (directory || !rule.DirectoryOnly) && rule.Pattern.IsMatch(path[rule.Under.Length..])) is { } last
        && !last.Negated;

    private static IEnumerable<Rule> Parse(string under, string line)
    {
        var escapedSpace = line.EndsWith("\\ ", StringComparison.Ordinal);
        var text = line.TrimEnd(' ');

        if (text.Length == 0 || text.StartsWith('#') || (text.EndsWith('\\') && !escapedSpace))
        {
            yield break;
        }

        text = escapedSpace ? text + " " : text;
        var negated = text.StartsWith('!');
        text = negated ? text[1..] : text;
        var directoryOnly = text.EndsWith('/');
        text = text.TrimEnd('/');
        var anchored = text.Contains('/');
        text = text.TrimStart('/');

        if (Glob(text) is { } glob && Compiled($"^{(anchored ? string.Empty : "(?:.*/)?")}{glob}$") is { } pattern)
        {
            yield return new Rule(under, pattern, negated, directoryOnly);
        }
    }

    private static Regex? Compiled(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? Glob(string pattern)
    {
        var regex = new StringBuilder();
        var index = 0;

        while (index < pattern.Length)
        {
            if (Piece(pattern, index) is not var (piece, length))
            {
                return null;
            }

            regex.Append(piece);
            index += length;
        }

        return regex.ToString();
    }

    private static (string Piece, int Length)? Piece(string pattern, int index)
    {
        var rest = pattern.AsSpan(index);
        var bounded = index == 0 || pattern[index - 1] == '/';

        if (bounded && rest.StartsWith("**/"))
        {
            return ("(?:.*/)?", 3);
        }

        if (bounded && rest.Length == 2 && rest.StartsWith("**"))
        {
            return (".*", 2);
        }

        return rest[0] switch
        {
            '\\' when rest.Length > 1 => (Regex.Escape(rest[1].ToString()), 2),
            '[' => Class(pattern, index),
            '*' => ("[^/]*", rest.StartsWith("**") ? 2 : 1),
            '?' => ("[^/]", 1),
            var letter => (Regex.Escape(letter.ToString()), 1),
        };
    }

    private static (string Piece, int Length)? Class(string pattern, int start)
    {
        var index = start + 1;
        var negated = index < pattern.Length && pattern[index] is '!' or '^';
        index += negated ? 1 : 0;
        var set = new StringBuilder();

        for (var first = true; index < pattern.Length && (first || pattern[index] != ']'); first = false)
        {
            var (member, length) = Member(pattern, index);
            set.Append(member);
            index += length;
        }

        return index >= pattern.Length ? null : ($"[{(negated ? "^/" : string.Empty)}{set}]", index - start + 1);
    }

    private static (string Member, int Length) Member(string pattern, int index)
    {
        if (pattern.AsSpan(index).StartsWith("[:") && pattern.IndexOf(":]", index + 2, StringComparison.Ordinal) is var end and > 0)
        {
            return (Posix(pattern[(index + 2)..end]), end + 2 - index);
        }

        return (pattern[index] is '\\' or ']' or '[' or '^' ? "\\" + pattern[index] : pattern[index].ToString(), 1);
    }

    private static string Posix(string name) => name switch
    {
        "digit" => "0-9",
        "alpha" => "a-zA-Z",
        "alnum" => "a-zA-Z0-9",
        "upper" => "A-Z",
        "lower" => "a-z",
        "space" => "\\s",
        "xdigit" => "0-9a-fA-F",
        _ => string.Empty,
    };

    internal sealed record Rule(string Under, Regex Pattern, bool Negated, bool DirectoryOnly);
}
