using Avala.CommandLines;

namespace Avala.Permissions.Policies;

internal static class ReadOnlyCommands
{
    public static bool Reads(ShellCommand command, Func<string, PermissionRequest> locate)
    {
        var arguments = command.Words.Select((word, index) => new Argument(word, index < command.Raws.Count ? command.Raws[index] : word)).ToList();

        if (arguments.Count == 0 || arguments.Any(argument => argument.Expands))
        {
            return false;
        }

        return arguments[0].Value switch
        {
            "find" => Finds(new Queue<Argument>(arguments.Skip(1)), locate),
            "git" => Git([.. arguments.Skip(1).SkipWhile(argument => argument.Value is "--no-pager" or "-P")], locate),
            var program => ReadOnlyOptions.Programs.TryGetValue(program, out var options) && new Scan(options, arguments.Skip(1)).Accepts(locate),
        };
    }

    public static bool Inside(string path, Func<string, PermissionRequest> locate) =>
        path.Length == 0 || (!path.StartsWith('~') && locate(path) is var located && (located.InsideWorkspace || located.Readable));

    private static bool Git(List<Argument> arguments, Func<string, PermissionRequest> locate) =>
        arguments.Count > 0 && ReadOnlyOptions.Git.TryGetValue(arguments[0].Value, out var git) && new Scan(git, arguments.Skip(1)).Accepts(locate);

    private static bool Finds(Queue<Argument> pending, Func<string, PermissionRequest> locate)
    {
        while (pending.TryPeek(out var first) && first.Value is "-H" or "-P")
        {
            _ = pending.Dequeue();
        }

        while (pending.TryPeek(out var start) && !Expression(start.Value))
        {
            if (!Inside(pending.Dequeue().Value, locate))
            {
                return false;
            }
        }

        while (pending.TryDequeue(out var argument))
        {
            if (!Primary(argument.Value, pending, locate))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Primary(string word, Queue<Argument> pending, Func<string, PermissionRequest> locate)
    {
        if (ReadOnlyOptions.FindFlags.Contains(word))
        {
            return true;
        }

        var newer = word.Length == 8 && word.StartsWith("-newer", StringComparison.Ordinal);
        var path = ReadOnlyOptions.FindPaths.Contains(word) || (newer && word[^1] != 't');

        return (path || newer || ReadOnlyOptions.FindValued.Contains(word))
            && pending.TryDequeue(out var value)
            && (!path || Inside(value.Value, locate));
    }

    private static bool Expression(string word) => word.StartsWith('-') || word is "(" or ")" or "!" or ",";

    private sealed class Scan(OptionSet options, IEnumerable<Argument> arguments)
    {
        private readonly Queue<Argument> pending = new(arguments);
        private readonly List<string> operands = [];
        private bool patternGiven;
        private bool ended;

        public bool Accepts(Func<string, PermissionRequest> locate)
        {
            if (options.Free)
            {
                return true;
            }

            while (pending.TryDequeue(out var argument))
            {
                if (!Takes(argument.Value))
                {
                    return false;
                }
            }

            return operands.Count <= options.Most
                && (!options.Paths || operands.Skip(options.TakesPattern && !patternGiven ? 1 : 0).All(operand => operand == "-" || Inside(operand, locate)));
        }

        private bool Takes(string word)
        {
            if (ended || word == "-" || !word.StartsWith('-'))
            {
                operands.Add(word);

                return true;
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
            patternGiven |= options.PatternOptions.Contains(name);

            var attached = word.Contains('=', StringComparison.Ordinal);

            if (options.Long.Contains(name))
            {
                return !attached || Safe(name, word.Split('=', 2)[1]);
            }

            if (options.LongAttached.Contains(name))
            {
                return attached && Safe(name, word.Split('=', 2)[1]);
            }

            var value = attached ? word.Split('=', 2)[1] : pending.TryDequeue(out var next) ? next.Value : null;

            return options.LongValued.Contains(name) && value is not null && Safe(name, value);
        }

        private static bool Safe(string name, string value) => !(name is "format" or "pretty" && value.Contains("%G", StringComparison.Ordinal));

        private bool Short(string word)
        {
            var letters = word[1..];

            if (options.Digits && letters.All(char.IsAsciiDigit))
            {
                return true;
            }

            for (var position = 0; position < letters.Length; position++)
            {
                if (options.Attached.Contains(letters[position], StringComparison.Ordinal))
                {
                    return true;
                }

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
