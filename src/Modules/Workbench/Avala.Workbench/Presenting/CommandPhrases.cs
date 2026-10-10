using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avala.Agents.Contracts.Events;

namespace Avala.Workbench.Presenting;

internal sealed record ShellCommand(string Name, IReadOnlyList<string> Writes)
{
    public string Summary => Writes.Count == 0 ? Name : $"{Name} > {string.Join(", ", Writes)}";
}

internal static partial class CommandPhrases
{
    private const int LineWidth = 80;

    private const int ShownWidth = 56;

    private static readonly HashSet<string> Prefixes = ["if", "then", "else", "elif", "do", "while", "until", "!", "{", "time"];

    private static readonly HashSet<string> Closings = ["fi", "done", "esac", "}", "in"];

    private static readonly HashSet<string> Headers = ["for", "case", "select", "function"];

    public static string OneLine(string text)
    {
        var lines = Lines(text);
        var first = lines[0].TrimEnd();
        var shown = first.Length > ShownWidth ? $"{first[..(ShownWidth - 1)].TrimEnd()}…" : first;

        return lines.Length > 1 ? string.Create(CultureInfo.InvariantCulture, $"{shown} +{lines.Length - 1} lines") : shown;
    }

    public static bool IsLong(string text) => Lines(text) is { Length: > 1 } || text.Trim().Length > LineWidth;

    public static string Title(ItemKind kind, string title, string command)
    {
        if (kind != ItemKind.Command || !IsLong(command))
        {
            return title;
        }

        var commands = Commands(command);

        return commands.Count > 1
            ? string.Create(CultureInfo.InvariantCulture, $"Run {commands.Count} commands: {string.Join(", ", commands.Select(found => found.Summary).Distinct(StringComparer.Ordinal))}")
            : $"Run {OneLine(command)}";
    }

    public static IReadOnlyList<string> Writes(ItemKind kind, string command) =>
        kind == ItemKind.Command ? [.. Commands(command).SelectMany(found => found.Writes).Distinct(StringComparer.Ordinal)] : [];

    public static IReadOnlyList<ShellCommand> Commands(string script) => new Reader(script.ReplaceLineEndings("\n")).Read();

    private static string[] Lines(string text) => text.Trim().ReplaceLineEndings("\n").Split('\n');

    [GeneratedRegex(@"^\d*(>>?|&>|>\|)(.*)$")]
    private static partial Regex Redirect();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*=")]
    private static partial Regex Assignment();

    private static ShellCommand[] Simple(IReadOnlyList<string> words)
    {
        var meaningful = words.Select(word => word.TrimStart('(').TrimEnd(')')).Where(word => word.Length > 0).ToList();
        var start = meaningful.FindIndex(word => !Prefixes.Contains(word) && !Assignment().IsMatch(word));

        if (start < 0 || Closings.Contains(meaningful[start]) || Headers.Contains(meaningful[start]))
        {
            return [];
        }

        var arguments = meaningful.Skip(start + 1).ToList();
        var writes = arguments
            .Zip(arguments.Skip(1).Append(string.Empty))
            .Select(pair => (Redirect: Redirect().Match(pair.First), pair.Second))
            .Where(pair => pair.Redirect.Success)
            .Select(pair => (pair.Redirect.Groups[2].Value is { Length: > 0 } inline ? inline : pair.Second).Trim('\'', '"'))
            .Where(target => target.Length > 0 && !target.StartsWith('&') && target != "/dev/null")
            .ToList();

        return [new ShellCommand(meaningful[start].Trim('\'', '"'), writes)];
    }

    private sealed class Reader(string script)
    {
        private readonly List<ShellCommand> commands = [];
        private readonly List<string> words = [];
        private readonly Queue<string> heredocs = [];
        private readonly StringBuilder word = new();
        private int at;

        public List<ShellCommand> Read()
        {
            while (at < script.Length)
            {
                Step(script[at]);
            }

            End();

            return commands;
        }

        private char Next(int ahead = 1) => at + ahead < script.Length ? script[at + ahead] : '\0';

        private void Step(char current)
        {
            switch (current)
            {
                case '\'' or '"':
                    Quoted(current);
                    break;
                case '\\':
                    word.Append(current).Append(Next());
                    at += 2;
                    break;
                case '$' when Next() == '(':
                    Nested();
                    break;
                case '#' when word.Length == 0:
                    SkipLine();
                    break;
                case ' ' or '\t':
                    Flush();
                    at++;
                    break;
                case '\n':
                    End();
                    at++;
                    SkipHeredocs();
                    break;
                case ';' or '|':
                case '&' when Next() != '>' && !word.ToString().EndsWith('>'):
                    End();
                    at += Next() == current ? 2 : 1;
                    break;
                case '<' when Next() == '<' && Next(2) == '<':
                    word.Append("<<<");
                    at += 3;
                    break;
                case '<' when Next() == '<':
                    Heredoc();
                    break;
                default:
                    word.Append(current);
                    at++;
                    break;
            }
        }

        private void Quoted(char quote)
        {
            word.Append(quote);
            at++;

            while (at < script.Length && script[at] != quote)
            {
                if (script[at] == '\\' && quote == '"')
                {
                    word.Append(script[at++]);
                }

                if (at < script.Length)
                {
                    word.Append(script[at++]);
                }
            }

            if (at < script.Length)
            {
                word.Append(script[at++]);
            }
        }

        private void Nested()
        {
            var depth = 0;

            do
            {
                depth += script[at] switch
                {
                    '(' => 1,
                    ')' => -1,
                    _ => 0,
                };
                word.Append(script[at++]);
            }
            while (at < script.Length && depth > 0);
        }

        private void Heredoc()
        {
            Flush();
            at += 2;

            while (at < script.Length && script[at] is '-' or ' ' or '\t')
            {
                at++;
            }

            var delimiter = new StringBuilder();

            while (at < script.Length && !char.IsWhiteSpace(script[at]) && script[at] is not (';' or '|' or '&' or '>' or '<'))
            {
                delimiter.Append(script[at++]);
            }

            heredocs.Enqueue(delimiter.ToString().Trim('\'', '"'));
        }

        private void SkipHeredocs()
        {
            while (heredocs.TryDequeue(out var delimiter))
            {
                while (at < script.Length)
                {
                    var end = script.IndexOf('\n', at) is var found and >= 0 ? found : script.Length;
                    var line = script[at..end].Trim();
                    at = Math.Min(end + 1, script.Length);

                    if (line == delimiter)
                    {
                        break;
                    }
                }
            }
        }

        private void SkipLine()
        {
            while (at < script.Length && script[at] != '\n')
            {
                at++;
            }
        }

        private void Flush()
        {
            if (word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }

        private void End()
        {
            Flush();

            if (words.Count > 0)
            {
                commands.AddRange(Simple(words));
                words.Clear();
            }
        }
    }
}
