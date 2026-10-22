using System.Text;
using Avala.Forges.Policy;
using Avala.Sdk;

namespace Avala.Forges.Watching;

internal static class GitPushes
{
    private const string Heads = "refs/heads/";

    private const string Then = "&&";

    private const string Next = ";";

    private const string Start = "";

    private static readonly HashSet<string> Pairs = new([Then, "||", "|&", ";;"], StringComparer.Ordinal);

    private static readonly HashSet<string> GlobalValues = new(["-C", "-c", "--git-dir", "--work-tree", "--namespace", "--exec-path"], StringComparer.Ordinal);

    private static readonly HashSet<string> PushValues = new(["-o", "--push-option", "--receive-pack", "--exec"], StringComparer.Ordinal);

    private static readonly HashSet<string> Commitless = new(["--dry-run", "--delete", "--mirror", "--prune"], StringComparer.Ordinal);

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

        return segments;
    }

    private static IEnumerable<PushedBranch> Push(List<string> words)
    {
        var index = 0;

        while (index < words.Count && words[index].Contains('=', StringComparison.Ordinal) && !words[index].StartsWith('-'))
        {
            index++;
        }

        if (index >= words.Count || !(words[index] == "git" || words[index].EndsWith("/git", StringComparison.Ordinal)))
        {
            return [];
        }

        index++;

        while (index < words.Count && words[index].StartsWith('-'))
        {
            index += GlobalValues.Contains(words[index]) ? 2 : 1;
        }

        if (index >= words.Count || words[index] != "push")
        {
            return [];
        }

        return Arguments(words, index + 1)
            .Bind(arguments => RemoteAddresses.Parse(arguments.Repository).Map(address => arguments.Refspecs.Select(Destination).Where(branch => branch.Length > 0).Select(branch => new PushedBranch(address, branch)).ToList()))
            .Match<IEnumerable<PushedBranch>>(found => found, () => []);
    }

    private static Option<(string Repository, List<string> Refspecs)> Arguments(List<string> words, int start)
    {
        var repository = string.Empty;
        var positional = new List<string>();
        var index = start;

        while (index < words.Count)
        {
            var word = words[index];

            if (LeavesNoCommit(word))
            {
                return Option<(string, List<string>)>.None;
            }

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

            index += word == "--repo" || PushValues.Contains(word) ? 2 : 1;
        }

        return repository.Length == 0 && positional.Count > 0 ? (positional[0], positional[1..]) : (repository, positional);
    }

    private static bool LeavesNoCommit(string word) =>
        Commitless.Contains(word) || (word.Length > 1 && word[0] == '-' && word[1] != '-' && word.AsSpan(1).IndexOfAny('n', 'd') >= 0);

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

            if (character is '(' or ')' or '`' or '{' or '}' or '$')
            {
                return false;
            }

            if (character is '\'' or '"')
            {
                return Quoted(character);
            }

            if (character == '\\' && Index + 1 < command.Length)
            {
                Take(command[Index + 1], 2);
            }
            else if (character is ' ' or '\t' or '\r')
            {
                End();
                Index++;
            }
            else if (character == '\n' || (character is '&' or '|' or ';' && !Redirecting()))
            {
                Separate(character);
            }
            else
            {
                Take(character, 1);
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

            if (closing < 0 || (quote == '"' && command.AsSpan(Index + 1, closing - Index - 1).IndexOfAny('\\', '$') >= 0))
            {
                return false;
            }

            current.Append(command, Index + 1, closing - Index - 1);
            started = true;
            Index = closing + 1;

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

        private bool Redirecting() =>
            command[Index] == '&' && ((current.Length > 0 && current[^1] is '>' or '<') || (Index + 1 < command.Length && command[Index + 1] == '>'));
    }
}
