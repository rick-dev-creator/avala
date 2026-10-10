using System.Buffers;
using System.Collections.Frozen;
using System.Text;

namespace Avala.CommandLines;

internal readonly record struct ShellWord(string Raw, string Value, bool Literal);

internal sealed class ShellReader(string text)
{
    private const int Deepest = 32;

    private static readonly FrozenSet<string> Discarded = FrozenSet.Create(StringComparer.Ordinal, "/dev/null", "/dev/stdout", "/dev/stderr");

    private static readonly SearchValues<char> Parameter = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_#@*!?$-");

    private static readonly (string Text, Redirection Kind)[] Operators =
    [
        ("&>>", Redirection.Output), ("&>", Redirection.Output), ("<<<", Redirection.Input), ("<<-", Redirection.Heredoc), ("<<", Redirection.Heredoc),
        ("<>", Redirection.Output), ("<&", Redirection.Input), ("<(", Redirection.Substitution), (">>", Redirection.Output), (">|", Redirection.Output),
        (">&", Redirection.Duplication), (">(", Redirection.Substitution), ("<", Redirection.Input), (">", Redirection.Output),
    ];

    private readonly List<ShellCommand> commands = [];
    private readonly List<string> writes = [];
    private List<string> redirected = [];
    private readonly Queue<(string Delimiter, bool Quoted, bool Tabs)> heredocs = new();
    private int at;
    private int depth;
    private bool opaque;
    private bool movesDirectory;

    public CommandLine Read()
    {
        ReadList(closing: false);

        return new CommandLine(
            commands,
            writes,
            opaque || heredocs.Count > 0 || (commands.Count == 0 && writes.Count == 0) || (movesDirectory && writes.Exists(path => !CommandLine.Rooted(path))),
            movesDirectory);
    }

    private static bool Diverges(char c) => c is '"' or '\'' or '`' or '$' or '#' or ';' or '&' or '|' or '<' or '>' or '(' or ')' or '{' or '}';

    private static bool Ends(char c) => c is ' ' or '\t' or '\n' or ';' or '&' or '|' or '(' or ')' or '<' or '>';

    private char Peek(int ahead = 0) => at + ahead < text.Length ? text[at + ahead] : '\0';

    private void ReadList(bool closing)
    {
        if (++depth > Deepest)
        {
            (opaque, at) = (true, text.Length);
        }

        while (true)
        {
            SkipBlanks();

            if (at >= text.Length)
            {
                opaque |= closing;
                depth--;

                return;
            }

            switch (text[at])
            {
                case ')' when closing:
                    at++;
                    depth--;

                    return;
                case ')':
                    (opaque, at) = (true, at + 1);
                    break;
                case '\n':
                    at++;
                    ReadHeredocs();
                    break;
                case ';' or '|' or '&' when Peek(1) != '>' || text[at] != '&':
                    at++;
                    break;
                default:
                    ReadCommand();
                    break;
            }
        }
    }

    private void ReadCommand()
    {
        var (words, outer) = (new List<ShellWord>(), redirected);
        redirected = [];

        while (!CommandEnds())
        {
            ReadPart(words);
        }

        SimpleCommands.Analyse(words, this, redirected);
        redirected = outer;
    }

    private bool CommandEnds()
    {
        SkipBlanks();

        return at >= text.Length || text[at] is '\n' or ';' or '|' or ')' || (text[at] == '&' && Peek(1) != '>');
    }

    private void ReadPart(List<ShellWord> words)
    {
        switch (text[at])
        {
            case '#':
                var end = text.IndexOf('\n', at);
                at = end < 0 ? text.Length : end;
                break;
            case '(' when words.Count == 0:
                at++;
                ReadList(closing: true);
                break;
            case '(':
                (opaque, at) = (true, at + 1);
                break;
            case '<' or '>' or '&':
                Redirect();
                break;
            default:
                ReadArgument(words);
                break;
        }
    }

    private void ReadArgument(List<ShellWord> words)
    {
        var word = ReadWord();

        if (word.Raw.Length > 0 && word.Raw.All(char.IsAsciiDigit) && Peek() is '<' or '>')
        {
            Redirect();
        }
        else if (word.Raw.Length > 0)
        {
            words.Add(word);
        }
    }

    private enum Redirection
    {
        Output,
        Input,
        Heredoc,
        Duplication,
        Substitution,
    }

    private void Redirect()
    {
        var (op, kind) = Operators.First(candidate => string.CompareOrdinal(text, at, candidate.Text, 0, candidate.Text.Length) == 0);
        opaque |= op == "<" && Peek(1) == '#';
        at += op.Length;

        if (kind == Redirection.Substitution)
        {
            opaque = true;
            ReadList(closing: true);

            return;
        }

        SkipBlanks();
        var target = at >= text.Length || Ends(text[at]) ? new ShellWord(string.Empty, string.Empty, Literal: false) : ReadWord();

        if (target.Raw.Length == 0)
        {
            opaque = true;
        }
        else if (kind == Redirection.Heredoc)
        {
            heredocs.Enqueue((target.Value, target.Raw.IndexOfAny(['\'', '"', '\\']) >= 0, op == "<<-"));
        }
        else if (kind == Redirection.Output || (kind == Redirection.Duplication && !IsDescriptor(target.Value)))
        {
            Write(target);
        }
    }

    private static bool IsDescriptor(string target) => target == "-" || target.All(char.IsAsciiDigit);

    private void Write(ShellWord target)
    {
        if (!target.Literal)
        {
            opaque = true;
        }
        else if (!Discarded.Contains(target.Value))
        {
            writes.Add(target.Value);
            redirected.Add(target.Value);
        }
    }

    private void ReadHeredocs()
    {
        while (heredocs.TryDequeue(out var heredoc))
        {
            ReadHeredoc(heredoc.Delimiter, heredoc.Quoted, heredoc.Tabs);
        }
    }

    private void ReadHeredoc(string delimiter, bool quoted, bool tabs)
    {
        while (at < text.Length)
        {
            var end = text.IndexOf('\n', at);
            var line = end < 0 ? text[at..] : text[at..end];
            at = end < 0 ? text.Length : end + 1;

            if ((tabs ? line.TrimStart('\t') : line) == delimiter)
            {
                return;
            }

            opaque |= !quoted && (line.Contains("$(", StringComparison.Ordinal) || line.Contains('`', StringComparison.Ordinal));
        }

        opaque = true;
    }

    private void SkipBlanks()
    {
        while (at < text.Length)
        {
            if (text[at] is ' ' or '\t')
            {
                at++;
            }
            else if (text[at] == '\\' && Peek(1) == '\n')
            {
                (opaque, at) = (true, at + 2);
            }
            else
            {
                return;
            }
        }
    }

    private ShellWord ReadWord()
    {
        var (raw, value, literal) = (new StringBuilder(), new StringBuilder(), true);

        while (at < text.Length && !Ends(text[at]))
        {
            var c = text[at];

            switch (c)
            {
                case '\\':
                    Escape(raw, value);
                    break;
                case '\'':
                    ReadSingle(raw, value);
                    break;
                case '"':
                    literal &= ReadDouble(raw, value);
                    break;
                case '$':
                    literal = false;
                    Dollar(raw, value, quoted: false);
                    break;
                case '`':
                    literal = false;
                    Backtick(raw);
                    break;
                default:
                    literal &= !Expands(c, first: raw.Length == 0);
                    raw.Append(c);
                    value.Append(c);
                    at++;
                    break;
            }
        }

        return new ShellWord(raw.ToString(), value.ToString(), literal);
    }

    private static bool Expands(char c, bool first) => c is '*' or '?' or '[' or '{' || (c == '~' && first);

    private void Escape(StringBuilder raw, StringBuilder value)
    {
        if (at + 1 >= text.Length || text[at + 1] == '\n')
        {
            (opaque, at) = (true, Math.Min(at + 2, text.Length));

            return;
        }

        opaque |= Diverges(text[at + 1]);
        raw.Append('\\').Append(text[at + 1]);
        value.Append(text[at + 1]);
        at += 2;
    }

    private void ReadSingle(StringBuilder raw, StringBuilder value)
    {
        var end = text.IndexOf('\'', at + 1);

        if (end < 0)
        {
            raw.Append(text[at..]);
            (opaque, at) = (true, text.Length);

            return;
        }

        raw.Append(text, at, end - at + 1);
        value.Append(text, at + 1, end - at - 1);
        at = end + 1;
    }

    private bool ReadDouble(StringBuilder raw, StringBuilder value)
    {
        var literal = true;
        raw.Append('"');
        at++;

        while (at < text.Length && text[at] != '"')
        {
            var c = text[at];

            if (c == '\\' && Peek(1) == '\n')
            {
                (opaque, at) = (true, at + 2);
            }
            else if (c == '\\' && Peek(1) is '$' or '`' or '"' or '\\')
            {
                opaque |= Diverges(text[at + 1]);
                raw.Append(c).Append(text[at + 1]);
                value.Append(text[at + 1]);
                at += 2;
            }
            else if (c == '$')
            {
                literal = false;
                Dollar(raw, value, quoted: true);
            }
            else if (c == '`')
            {
                literal = false;
                Backtick(raw);
            }
            else
            {
                raw.Append(c);
                value.Append(c);
                at++;
            }
        }

        if (at >= text.Length)
        {
            opaque = true;

            return false;
        }

        raw.Append('"');
        at++;

        return literal;
    }

    private void Dollar(StringBuilder raw, StringBuilder value, bool quoted)
    {
        var start = at;

        switch (Peek(1))
        {
            case '(':
                at += 2;
                opaque = true;
                ReadList(closing: true);
                break;
            case '{':
                var end = text.IndexOf('}', at + 2);
                opaque |= end < at + 3 || text.AsSpan(at + 2, end - at - 2).ContainsAnyExcept(Parameter);
                at = end < 0 ? text.Length : end + 1;
                break;
            case '\'' when !quoted:
                at += 2;
                while (at < text.Length && text[at] != '\'')
                {
                    at += text[at] == '\\' ? 2 : 1;
                }

                opaque |= at >= text.Length;
                at = Math.Min(at + 1, text.Length);
                break;
            default:
                at++;
                break;
        }

        raw.Append(text, start, at - start);
        value.Append(text, start, at - start);
    }

    private void Backtick(StringBuilder raw)
    {
        var inner = new StringBuilder();
        var start = at++;

        while (at < text.Length && text[at] != '`')
        {
            if (text[at] == '\\' && Peek(1) is '$' or '`' or '\\')
            {
                at++;
            }

            inner.Append(text[at]);
            at++;
        }

        at = Math.Min(at + 1, text.Length);
        raw.Append(text, start, at - start);
        Merge(inner.ToString(), opaque: true);
    }

    public void Found(List<ShellWord> words, IReadOnlyList<string> written, bool restated) =>
        commands.Add(new ShellCommand(string.Join(' ', words.Select(word => word.Raw)), [.. words.Select(word => word.Value)], written, restated));

    public void Unanalysable() => opaque = true;

    public void MovesDirectory() => movesDirectory = true;

    public void Merge(string script, bool opaque)
    {
        var line = depth < Deepest ? new ShellReader(script) { depth = depth + 1 }.Read() : new CommandLine([], [], Opaque: true, MovesDirectory: false);
        commands.AddRange(line.Commands);
        writes.AddRange(line.Writes);
        this.opaque |= opaque || line.Opaque;
        movesDirectory |= line.MovesDirectory;
    }
}
