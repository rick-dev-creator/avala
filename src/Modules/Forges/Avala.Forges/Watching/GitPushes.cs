using Avala.Forges.Policy;

namespace Avala.Forges.Watching;

internal static class GitPushes
{
    private const string Separator = ";";

    private const string Heads = "refs/heads/";

    private static readonly HashSet<string> GlobalValues = new(["-C", "-c", "--git-dir", "--work-tree", "--namespace", "--exec-path"], StringComparer.Ordinal);

    private static readonly HashSet<string> PushValues = new(["-o", "--push-option", "--receive-pack", "--exec"], StringComparer.Ordinal);

    public static IReadOnlyList<PushedBranch> In(string command)
    {
        var words = Words(command);
        var pushes = new List<PushedBranch>();

        for (var index = 0; index < words.Count; index++)
        {
            if (words[index] == "git" || words[index].EndsWith("/git", StringComparison.Ordinal))
            {
                pushes.AddRange(Push(words, index + 1));
            }
        }

        return [.. pushes.Distinct()];
    }

    private static IEnumerable<PushedBranch> Push(List<string> words, int start)
    {
        var index = start;

        while (index < words.Count && words[index].StartsWith('-'))
        {
            index += GlobalValues.Contains(words[index]) ? 2 : 1;
        }

        if (index >= words.Count || words[index] != "push")
        {
            return [];
        }

        var (repository, positional) = Arguments(words, index + 1);

        return RemoteAddresses.Parse(repository).Match(
            address => positional.Select(Destination).Where(branch => branch.Length > 0).Select(branch => new PushedBranch(address, branch)),
            () => []);
    }

    private static (string Repository, List<string> Refspecs) Arguments(List<string> words, int start)
    {
        var repository = string.Empty;
        var positional = new List<string>();
        var index = start;

        while (index < words.Count && words[index] != Separator)
        {
            var word = words[index];
            var takesValue = word == "--repo" || PushValues.Contains(word);

            if (word.StartsWith("--repo=", StringComparison.Ordinal))
            {
                repository = word["--repo=".Length..];
            }
            else if (word == "--repo" && index + 1 < words.Count)
            {
                repository = words[index + 1];
            }
            else if (!word.StartsWith('-') && word.IndexOfAny(['<', '>']) < 0)
            {
                positional.Add(word);
            }

            index += takesValue ? 2 : 1;
        }

        return repository.Length == 0 && positional.Count > 0 ? (positional[0], positional[1..]) : (repository, positional);
    }

    private static string Destination(string refspec)
    {
        var spec = refspec.TrimStart('+');
        var colon = spec.LastIndexOf(':');
        var destination = colon >= 0 ? spec[(colon + 1)..] : spec;

        if (destination.StartsWith(Heads, StringComparison.Ordinal))
        {
            return destination[Heads.Length..];
        }

        return destination.Length == 0 || destination == "HEAD" || destination.StartsWith("refs/", StringComparison.Ordinal) ? string.Empty : destination;
    }

    private static List<string> Words(string command)
    {
        var spaced = command;

        foreach (var separator in new[] { "&&", "||", ";", "|", "&", "\n" })
        {
            spaced = spaced.Replace(separator, $" {Separator} ", StringComparison.Ordinal);
        }

        return [.. spaced.Split((char[])[' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries).Select(word => word.Trim('"', '\''))];
    }
}
