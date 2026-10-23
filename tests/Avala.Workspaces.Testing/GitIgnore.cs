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

        foreach (var file in Directory.EnumerateFiles(folder, FileName, new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }).Order(StringComparer.Ordinal))
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
        var text = line.TrimEnd(' ');

        if (text.Length == 0 || text.StartsWith('#'))
        {
            yield break;
        }

        var negated = text.StartsWith('!');
        text = negated ? text[1..] : text;
        var directoryOnly = text.EndsWith('/');
        text = text.TrimEnd('/');
        var anchored = text.Contains('/');
        text = text.TrimStart('/');

        yield return new Rule(under, new Regex($"^{(anchored ? string.Empty : "(?:.*/)?")}{Glob(text)}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)), negated, directoryOnly);
    }

    private static string Glob(string pattern)
    {
        var regex = new StringBuilder();
        var index = 0;

        while (index < pattern.Length)
        {
            if (pattern.AsSpan(index).StartsWith("**/"))
            {
                regex.Append("(?:.*/)?");
                index += 3;
            }
            else if (pattern.AsSpan(index).StartsWith("**"))
            {
                regex.Append(".*");
                index += 2;
            }
            else
            {
                regex.Append(pattern[index] switch
                {
                    '*' => "[^/]*",
                    '?' => "[^/]",
                    var letter => Regex.Escape(letter.ToString()),
                });
                index++;
            }
        }

        return regex.ToString();
    }

    internal sealed record Rule(string Under, Regex Pattern, bool Negated, bool DirectoryOnly);
}
