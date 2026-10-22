using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;
using Avala.Forges.Policy;
using Avala.Sdk;

namespace Avala.Forges.Watching;

internal static partial class GitPushes
{
    private const string Heads = "refs/heads/";

    private const string Then = "&&";

    private const string Next = ";";

    private const string Start = "";

    private static readonly SearchValues<char> Substitutions = SearchValues.Create("$()`{}");

    private static readonly SearchValues<char> Blanks = SearchValues.Create(" \t\r");

    private static readonly SearchValues<char> Operators = SearchValues.Create("&|;");

    private static readonly HashSet<string> Pairs = new([Then, "||", "|&", ";;"], StringComparer.Ordinal);

    private static readonly HashSet<string> GlobalValues = new(["-C", "-c", "--git-dir", "--work-tree", "--namespace", "--exec-path"], StringComparer.Ordinal);

    private static readonly Dictionary<string, Kind> LongOptions = new(StringComparer.Ordinal)
    {
        ["repo"] = Kind.Repository,
        ["receive-pack"] = Kind.Value,
        ["exec"] = Kind.Value,
        ["push-option"] = Kind.Value,
        ["recurse-submodules"] = Kind.Submodules,
        ["all"] = Kind.Commitless,
        ["branches"] = Kind.Commitless,
        ["mirror"] = Kind.Commitless,
        ["delete"] = Kind.Commitless,
        ["dry-run"] = Kind.Commitless,
        ["prune"] = Kind.Commitless,
        ["tags"] = Kind.Flag,
        ["porcelain"] = Kind.Flag,
        ["force"] = Kind.Flag,
        ["force-with-lease"] = Kind.Flag,
        ["force-if-includes"] = Kind.Flag,
        ["thin"] = Kind.Flag,
        ["set-upstream"] = Kind.Flag,
        ["progress"] = Kind.Flag,
        ["verify"] = Kind.Flag,
        ["follow-tags"] = Kind.Flag,
        ["signed"] = Kind.Flag,
        ["atomic"] = Kind.Flag,
        ["ipv4"] = Kind.Flag,
        ["ipv6"] = Kind.Flag,
        ["quiet"] = Kind.Flag,
        ["verbose"] = Kind.Flag,
    };

    private enum Kind
    {
        Flag,
        Value,
        Repository,
        Submodules,
        Commitless,
    }

    public static IReadOnlyList<PushedBranch> In(string command)
    {
        return Lexed(command).Match(words => Found(Segments(words)), () => []);
    }

    private static IReadOnlyList<PushedBranch> Found(List<Segment> segments)
    {
        var pushes = new List<PushedBranch>();

        for (var index = 0; index < segments.Count; index++)
        {
            if (segments[index].Before is Start or Next or Then && segments.Skip(index).All(next => next.After is Start or Then))
            {
                pushes.AddRange(Push(segments[index].Words));
            }
        }

        return [.. pushes.Distinct()];
    }

    private static List<Segment> Segments(List<Word> words)
    {
        var segments = new List<Segment>();
        var before = Start;
        var current = new List<string>();

        foreach (var word in words)
        {
            if (word.Operator)
            {
                segments.Add(new Segment(before, current, word.Text));
                before = word.Text;
                current = [];
            }
            else
            {
                current.Add(word.Text);
            }
        }

        segments.Add(new Segment(before, current, Start));

        while (segments.Count > 1 && segments[^1].Words.Count == 0 && segments[^1].Before == Next)
        {
            segments.RemoveAt(segments.Count - 1);
            segments[^1] = segments[^1] with { After = Start };
        }

        return segments;
    }

    private static IEnumerable<PushedBranch> Push(List<string> words) =>
        Subcommand(words) is var index && index < words.Count && words[index] == "push"
            ? Arguments(words, index + 1).Bind(Branches).Match<IEnumerable<PushedBranch>>(found => found, () => [])
            : [];

    private static int Subcommand(List<string> words)
    {
        var index = 0;

        while (index < words.Count && Assignment(words[index]))
        {
            index++;
        }

        if (index >= words.Count || !Git(words[index]))
        {
            return words.Count;
        }

        index++;

        while (index < words.Count && words[index].StartsWith('-'))
        {
            index += GlobalValues.Contains(words[index]) ? 2 : 1;
        }

        return index;
    }

    private static bool Assignment(string word) => word.Contains('=', StringComparison.Ordinal) && !word.StartsWith('-');

    private static bool Git(string word) => word == "git" || word.EndsWith("/git", StringComparison.Ordinal);

    private static Option<List<PushedBranch>> Branches((string Repository, List<string> Refspecs) arguments) =>
        RemoteAddresses.Parse(arguments.Repository)
            .Map(address => arguments.Refspecs.Select(Destination).Where(branch => branch.Length > 0).Select(branch => new PushedBranch(address, branch)).ToList());

    private static Option<(string Repository, List<string> Refspecs)> Arguments(List<string> words, int start)
    {
        var named = string.Empty;
        var positional = new List<string>();
        var index = start;

        while (index < words.Count)
        {
            var word = words[index];

            if (word == "--")
            {
                positional.AddRange(words.Skip(index + 1));

                break;
            }

            var parsed = Optional(word) ? Parsed(words, index) : (1, string.Empty);

            if (parsed.IsNone)
            {
                return Option<(string, List<string>)>.None;
            }

            var (length, repository) = parsed.Match(taken => taken, () => (1, string.Empty));
            named = repository.Length > 0 ? repository : named;

            if (!Optional(word) && word.IndexOfAny(['<', '>']) < 0)
            {
                positional.Add(word);
            }

            index += length;
        }

        return positional.Count > 0 ? (positional[0], positional[1..]) : (named, positional);
    }

    private static bool Optional(string word) => word.StartsWith('-') && word != "-";

    private static Option<(int Length, string Repository)> Parsed(List<string> words, int index)
    {
        var word = words[index];

        if (!word.StartsWith("--", StringComparison.Ordinal))
        {
            return Short(words, index);
        }

        var equals = word.IndexOf('=', StringComparison.Ordinal);
        var name = equals < 0 ? word[2..] : word[2..equals];

        if (name.StartsWith("no-", StringComparison.Ordinal) && Resolved(name).IsNone)
        {
            return Resolved(name[3..]).Map(_ => (1, string.Empty));
        }

        return Resolved(name).Bind(kind => Taken(words, index, equals, kind));
    }

    private static Option<(int Length, string Repository)> Taken(List<string> words, int index, int equals, Kind kind)
    {
        var separate = equals < 0 && kind is Kind.Value or Kind.Repository or Kind.Submodules;
        var value = equals >= 0 ? words[index][(equals + 1)..] : separate && index + 1 < words.Count ? words[index + 1] : string.Empty;

        return kind switch
        {
            Kind.Commitless => Option<(int, string)>.None,
            Kind.Submodules when value == "only" => Option<(int, string)>.None,
            Kind.Repository => (separate ? 2 : 1, value),
            _ => (separate ? 2 : 1, string.Empty),
        };
    }

    private static Option<(int Length, string Repository)> Short(List<string> words, int index)
    {
        var cluster = words[index].AsSpan(1);

        for (var position = 0; position < cluster.Length; position++)
        {
            switch (cluster[position])
            {
                case 'o':
                    return (position == cluster.Length - 1 ? 2 : 1, string.Empty);
                case 'u' or 'f' or 'q' or 'v' or '4' or '6':
                    continue;
                default:
                    return Option<(int, string)>.None;
            }
        }

        return (1, string.Empty);
    }

    private static Option<Kind> Resolved(string name)
    {
        if (LongOptions.TryGetValue(name, out var exact))
        {
            return exact;
        }

        var candidates = name.Length == 0 ? [] : LongOptions.Where(option => option.Key.StartsWith(name, StringComparison.Ordinal)).Select(option => option.Value).ToList();

        return candidates.Count == 1 ? candidates[0] : Option<Kind>.None;
    }

    private static string Destination(string refspec)
    {
        var spec = refspec.TrimStart('+');
        var colon = spec.LastIndexOf(':');

        if (colon == 0)
        {
            return string.Empty;
        }

        var destination = colon > 0 ? spec[(colon + 1)..] : spec;

        if (destination.StartsWith(Heads, StringComparison.Ordinal))
        {
            return destination[Heads.Length..];
        }

        return destination.Length == 0 || destination == "HEAD" || destination.StartsWith("refs/", StringComparison.Ordinal) ? string.Empty : destination;
    }

    private static Option<List<Word>> Lexed(string command)
    {
        var lexer = new Lexer(command);

        while (lexer.Index < command.Length)
        {
            if (!lexer.Step())
            {
                return Option<List<Word>>.None;
            }
        }

        lexer.End();

        return lexer.Words;
    }

    private static int Credential(string prefix, string text, int at)
    {
        var variable = Variable().Match(text, at);

        return variable.Success && UserInfo().IsMatch(prefix) && Authority().IsMatch(text[(at + variable.Length)..]) ? variable.Length : 0;
    }

    [GeneratedRegex(@"\G\$(\{[A-Za-z_][A-Za-z0-9_]*\}|[A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex Variable();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]*://[^/@?#\s$]*$")]
    private static partial Regex UserInfo();

    [GeneratedRegex(@"^[^/@?#\s""'$`]*@")]
    private static partial Regex Authority();

    private readonly record struct Word(string Text, bool Operator);

    private sealed record Segment(string Before, List<string> Words, string After);

    private sealed class Lexer(string command)
    {
        private readonly StringBuilder current = new();
        private bool started;

        public List<Word> Words { get; } = [];

        public int Index { get; private set; }

        public bool Step()
        {
            var character = command[Index];

            if (Substitutions.Contains(character))
            {
                return character == '$' && Credentialed();
            }

            if (character is '\'' or '"')
            {
                return Quoted(character);
            }

            if (character is '#' or '\\' && Escaped(character))
            {
                return true;
            }

            if (Blanks.Contains(character))
            {
                End();
                Index++;
            }
            else if (Separates(character))
            {
                Separate(character);
            }
            else
            {
                Take(character, 1);
            }

            return true;
        }

        private bool Escaped(char character)
        {
            if (character == '#')
            {
                if (started)
                {
                    return false;
                }

                var end = command.IndexOf('\n', Index);
                Index = end < 0 ? command.Length : end;
            }
            else if (command.AsSpan(Index).StartsWith("\\\n"))
            {
                Index += 2;
            }
            else if (command.AsSpan(Index).StartsWith("\\\r\n"))
            {
                Index += 3;
            }
            else if (Index + 1 < command.Length)
            {
                Take(command[Index + 1], 2);
            }
            else
            {
                return false;
            }

            return true;
        }

        public void End()
        {
            if (started)
            {
                Words.Add(new Word(current.ToString(), Operator: false));
                current.Clear();
                started = false;
            }
        }

        private bool Quoted(char quote)
        {
            var closing = command.IndexOf(quote, Index + 1);

            if (closing < 0 || (quote == '"' && !Plain(command[(Index + 1)..closing])))
            {
                return false;
            }

            current.Append(command, Index + 1, closing - Index - 1);
            started = true;
            Index = closing + 1;

            return true;
        }

        private bool Plain(string content)
        {
            if (content.AsSpan().IndexOfAny('\\', '`') >= 0)
            {
                return false;
            }

            for (var at = content.IndexOf('$', StringComparison.Ordinal); at >= 0; at = content.IndexOf('$', at + 1))
            {
                if (Credential(current + content[..at], content, at) == 0)
                {
                    return false;
                }
            }

            return true;
        }

        private bool Credentialed()
        {
            var length = Credential(current.ToString(), command, Index);

            if (length == 0)
            {
                return false;
            }

            current.Append(command, Index, length);
            started = true;
            Index += length;

            return true;
        }

        private void Take(char character, int length)
        {
            current.Append(character);
            started = true;
            Index += length;
        }

        private void Separate(char character)
        {
            End();
            var length = Index + 1 < command.Length && Pairs.Contains(command.Substring(Index, 2)) ? 2 : 1;
            Words.Add(new Word(character == '\n' ? Next : command.Substring(Index, length), Operator: true));
            Index += length;
        }

        private bool Separates(char character) => character == '\n' || (Operators.Contains(character) && !Redirecting());

        private bool Redirecting() =>
            command[Index] == '&' && ((current.Length > 0 && current[^1] is '>' or '<') || (Index + 1 < command.Length && command[Index + 1] == '>'));
    }
}
