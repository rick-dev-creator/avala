using Avala.CommandLines;

namespace Avala.Permissions.Policies;

internal static class ReadOnlyCommands
{
    public static bool Reads(ShellCommand command, Func<string, PermissionRequest> locate)
    {
        var words = command.Words;

        if (words.Count == 0 || words.Any(word => word.Contains('$', StringComparison.Ordinal)))
        {
            return false;
        }

        return words[0] switch
        {
            "find" => Finds(new Queue<string>(words.Skip(1)), locate),
            "git" => words.Count > 1 && ReadOnlyOptions.Git.TryGetValue(words[1], out var git) && new Scan(git, words.Skip(2)).Accepts(locate),
            var program => ReadOnlyOptions.Programs.TryGetValue(program, out var options) && new Scan(options, words.Skip(1)).Accepts(locate),
        };
    }

    public static string Unquoted(string word) => new([.. word.Where(character => character is not ('\'' or '"' or '\\'))]);

    public static bool Globbed(string raw) => raw.Length > 0 && raw[0] is '*' or '?' or '[';

    public static bool Inside(string path, Func<string, PermissionRequest> locate) =>
        path.Length == 0 || (!path.StartsWith('~') && locate(path) is var located && (located.InsideWorkspace || located.Readable));

    private static bool Finds(Queue<string> pending, Func<string, PermissionRequest> locate)
    {
        while (pending.TryPeek(out var first) && Unquoted(first) is "-H" or "-L" or "-P")
        {
            _ = pending.Dequeue();
        }

        while (pending.TryPeek(out var start) && !Expression(Unquoted(start)))
        {
            if (Globbed(pending.Dequeue()) || !Inside(Unquoted(start), locate))
            {
                return false;
            }
        }

        while (pending.TryDequeue(out var raw))
        {
            if (!Primary(raw, pending, locate))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Primary(string raw, Queue<string> pending, Func<string, PermissionRequest> locate)
    {
        var word = Unquoted(raw);

        if (Globbed(raw))
        {
            return false;
        }

        if (ReadOnlyOptions.FindFlags.Contains(word))
        {
            return true;
        }

        var newer = word.Length == 8 && word.StartsWith("-newer", StringComparison.Ordinal);
        var path = ReadOnlyOptions.FindPaths.Contains(word) || (newer && word[^1] != 't');

        return (path || newer || ReadOnlyOptions.FindValued.Contains(word))
            && pending.TryDequeue(out var value)
            && (!path || Inside(Unquoted(value), locate));
    }

    private static bool Expression(string word) => word.StartsWith('-') || word is "(" or ")" or "!" or ",";

    private sealed class Scan(OptionSet options, IEnumerable<string> words)
    {
        private readonly Queue<string> pending = new(words);
        private readonly List<string> operands = [];
        private bool patternGiven;
        private bool ended;

        public bool Accepts(Func<string, PermissionRequest> locate)
        {
            if (options.Free)
            {
                return true;
            }

            while (pending.TryDequeue(out var raw))
            {
                if (!Takes(raw))
                {
                    return false;
                }
            }

            return operands.Count <= options.Most
                && (!options.Paths || operands.Skip(options.TakesPattern && !patternGiven ? 1 : 0).All(operand => operand == "-" || Inside(operand, locate)));
        }

        private bool Takes(string raw)
        {
            var word = Unquoted(raw);

            if (ended || word == "-" || !word.StartsWith('-'))
            {
                operands.Add(word);

                return ended || !Globbed(raw);
            }

            if (word == "--")
            {
                ended = true;

                return true;
            }

            return word.StartsWith("--", StringComparison.Ordinal) ? Long(word) : Short(word);
        }

        private bool Long(string word)
        {
            var name = word[2..].Split('=', 2)[0];

            if (options.Long.Contains(name))
            {
                return true;
            }

            patternGiven |= options.PatternOptions.Contains(name);

            return options.LongValued.Contains(name) && (word.Contains('=', StringComparison.Ordinal) || pending.TryDequeue(out _));
        }

        private bool Short(string word)
        {
            var letters = word[1..];

            if (options.Digits && letters.All(char.IsAsciiDigit))
            {
                return true;
            }

            for (var position = 0; position < letters.Length; position++)
            {
                if (options.Valued.Contains(letters[position], StringComparison.Ordinal))
                {
                    patternGiven |= options.TakesPattern && letters[position] == 'e';

                    return position + 1 < letters.Length || pending.TryDequeue(out _);
                }

                if (!options.Flags.Contains(letters[position], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
