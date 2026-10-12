using System.Collections.Frozen;
using Avala.CommandLines;

namespace Avala.Permissions.Policies;

internal static class ReadOnlyCommands
{
    private static readonly Reader Plain = new([]);

    private static readonly Reader Patterns = new([]) { TakesPattern = true };

    private static readonly Reader Printing = new([]) { Paths = false };

    private static readonly FrozenDictionary<string, Reader> Programs = new Dictionary<string, Reader>
    {
        ["ls"] = Plain,
        ["cat"] = Plain,
        ["head"] = Plain,
        ["tail"] = Plain,
        ["wc"] = Plain,
        ["stat"] = Plain,
        ["cut"] = Plain,
        ["grep"] = Patterns,
        ["egrep"] = Patterns,
        ["fgrep"] = Patterns,
        ["rg"] = new(["--pre", "--pre-glob"]) { TakesPattern = true },
        ["find"] = new(["-exec", "-execdir", "-ok", "-okdir", "-delete", "-fprint", "-fprint0", "-fprintf", "-fls"]),
        ["file"] = new(["-C", "--compile"]),
        ["tree"] = new(["-o"]),
        ["sort"] = new(["-o", "--output", "--compress-program"]),
        ["uniq"] = new([]) { Most = 1 },
        ["pwd"] = Printing,
        ["echo"] = Printing,
        ["tr"] = Printing,
        ["git"] = new(["--output", "--ext-diff"]) { Subcommands = ["status", "log", "diff", "show"] },
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static bool Reads(ShellCommand command, Func<string, PermissionRequest> locate) =>
        command.Words.Count > 0
        && Programs.TryGetValue(command.Words[0], out var reader)
        && Invokes(command.Words, reader)
        && Allows(reader, [.. command.Words.Skip(reader.Skipped)], locate);

    private static bool Invokes(IReadOnlyList<string> words, Reader reader) =>
        reader.Skipped <= words.Count
        && (reader.Subcommands.Length == 0 || reader.Subcommands.Contains(words[1], StringComparer.Ordinal));

    private static bool Allows(Reader reader, List<string> rest, Func<string, PermissionRequest> locate)
    {
        var operands = Operands(rest).ToList();

        return !rest.Exists(word => word.Contains('$', StringComparison.Ordinal) || reader.Forbids(word))
            && operands.Count <= reader.Most
            && (!reader.Paths || StayInside(reader, rest, operands, locate));
    }

    private static bool StayInside(Reader reader, List<string> rest, List<string> operands, Func<string, PermissionRequest> locate) =>
        operands.Skip(PatternFirst(rest, reader) ? 1 : 0).All(word => Inside(word, locate))
        && rest.Where(Option).All(word => Inside(word, locate));

    private static bool PatternFirst(List<string> words, Reader reader) =>
        reader.TakesPattern && !words.Exists(word => word.StartsWith("--regexp", StringComparison.Ordinal)
            || word.StartsWith("--file", StringComparison.Ordinal)
            || (Short(word) && word.AsSpan(1).IndexOfAny('e', 'f') >= 0));

    private static IEnumerable<string> Operands(List<string> words)
    {
        var ended = false;

        foreach (var word in words)
        {
            if (!ended && word == "--")
            {
                ended = true;
            }
            else if ((ended || !word.StartsWith('-')) && word != "-")
            {
                yield return word;
            }
        }
    }

    private static bool Option(string word) => word.StartsWith('-') && word.Length > 1;

    private static bool Short(string word) => word.Length > 1 && word[0] == '-' && word[1] != '-';

    private static bool Inside(string word, Func<string, PermissionRequest> locate)
    {
        var path = Option(word) ? Valued(word) : word;

        return path.Length == 0
            || (Option(word) && !path.Contains('/', StringComparison.Ordinal) && !path.StartsWith('~'))
            || (!path.StartsWith('~') && locate(path).InsideWorkspace);
    }

    private static string Valued(string option) =>
        option.IndexOf('=', StringComparison.Ordinal) is var equals and >= 0 ? option[(equals + 1)..]
        : Short(option) && option.Contains('/', StringComparison.Ordinal) ? option[option.IndexOf('/', StringComparison.Ordinal)..]
        : string.Empty;

    private sealed record Reader(string[] Forbidden)
    {
        public bool TakesPattern { get; init; }

        public bool Paths { get; init; } = true;

        public int Most { get; init; } = int.MaxValue;

        public string[] Subcommands { get; init; } = [];

        public int Skipped => Subcommands.Length > 0 ? 2 : 1;

        public bool Forbids(string word) =>
            Forbidden.Any(option => word == option
                || word.StartsWith(option + "=", StringComparison.Ordinal)
                || (option.Length == 2 && Short(word) && word.AsSpan(1).Contains(option[1])));
    }
}
