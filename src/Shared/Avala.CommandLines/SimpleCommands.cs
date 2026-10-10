using System.Buffers;
using System.Collections.Frozen;

namespace Avala.CommandLines;

internal static class SimpleCommands
{
    private static readonly FrozenSet<string> Reserved = FrozenSet.Create(
        StringComparer.Ordinal,
        "if", "then", "else", "elif", "fi", "for", "while", "until", "do", "done", "case", "esac", "select", "function", "coproc", "[[", "]]", "{", "}");

    private static readonly FrozenSet<string> Shells = FrozenSet.Create(StringComparer.Ordinal, "sh", "bash", "zsh", "dash", "ksh", "mksh", "ash");

    private static readonly SearchValues<char> Name = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_");

    public static void Analyse(List<ShellWord> words, ShellReader reader, IReadOnlyList<string> written) =>
        Analyse(words, reader, written, restated: false);

    private static void Analyse(List<ShellWord> words, ShellReader reader) => Analyse(words, reader, [], restated: false);

    private static void Analyse(List<ShellWord> words, ShellReader reader, IReadOnlyList<string> written, bool restated)
    {
        if (words.Count == 0)
        {
            return;
        }

        reader.Found(words, written, restated);
        var name = words[0];

        if (IsAssignment(name.Value))
        {
            Analyse([.. words.Skip(1)], reader, [], restated: true);
        }
        else if (!name.Literal || Reserved.Contains(name.Value))
        {
            reader.Unanalysable();
        }
        else
        {
            var program = name.Value[(name.Value.LastIndexOf('/') + 1)..];

            if (program != name.Value)
            {
                reader.Found([new ShellWord(program, program, Literal: true), .. words.Skip(1)], [], restated: true);
            }

            Wrapped(program, words, reader);
        }
    }

    private static void Wrapped(string program, List<ShellWord> words, ShellReader reader)
    {
        switch (program)
        {
            case "cd" or "pushd" or "popd":
                reader.MovesDirectory();
                break;
            case "eval" or "trap":
                reader.Merge(string.Join(' ', words.Skip(1).Select(word => word.Value)), opaque: true);
                break;
            case "sudo" or "doas" or "su":
                reader.Unanalysable();
                Analyse(After(words, "-u", "-g", "-C", "-D", "-h", "-p", "-r", "-t", "-U", "-T"), reader);
                break;
            case "env":
                Environment(words, reader);
                break;
            case "xargs":
                Analyse(After(words, "-a", "-d", "-E", "-I", "-L", "-n", "-P", "-s"), reader);
                break;
            case "timeout":
                Analyse([.. After(words, "-s", "-k").Skip(1)], reader);
                break;
            case "nice":
                Analyse(After(words, "-n"), reader);
                break;
            case "exec":
                Analyse(After(words, "-a"), reader);
                break;
            case "stdbuf":
                Analyse(After(words, "-i", "-o", "-e"), reader);
                break;
            case "nohup" or "time" or "command" or "builtin" or "!":
                Analyse(After(words), reader);
                break;
            case "find":
                Executed(words, reader);
                break;
            case var shell when Shells.Contains(shell):
                Shell(words, reader);
                break;
        }
    }

    private static bool IsAssignment(string word)
    {
        var equals = word.IndexOf('=', StringComparison.Ordinal);

        return equals > 0 && !char.IsAsciiDigit(word[0]) && !word.AsSpan(0, equals).ContainsAnyExcept(Name);
    }

    private static List<ShellWord> After(List<ShellWord> words, params string[] valued)
    {
        var index = 1;

        while (index < words.Count && words[index].Value.Length > 1 && words[index].Value.StartsWith('-'))
        {
            if (words[index].Value == "--")
            {
                index++;

                break;
            }

            index += valued.Contains(words[index].Value) ? 2 : 1;
        }

        return [.. words.Skip(index)];
    }

    private static void Environment(List<ShellWord> words, ShellReader reader)
    {
        if (words.Exists(word => Option(word, "-C", "--chdir")))
        {
            reader.MovesDirectory();
        }

        if (words.Exists(word => Option(word, "-S", "--split-string")))
        {
            reader.Unanalysable();
        }

        Analyse(After(words, "-u", "-C", "-S"), reader);
    }

    private static bool Option(ShellWord word, string brief, string full) =>
        word.Value == brief || word.Value == full || word.Value.StartsWith(full + "=", StringComparison.Ordinal);

    private static void Executed(List<ShellWord> words, ShellReader reader)
    {
        var index = 1;

        while (index < words.Count)
        {
            if (words[index].Value is "-exec" or "-execdir" or "-ok" or "-okdir")
            {
                var end = words.FindIndex(index + 1, word => word.Value is ";" or "+");
                end = end < 0 ? words.Count : end;
                Analyse([.. words.Skip(index + 1).Take(end - index - 1)], reader);
                index = end;
            }

            index++;
        }
    }

    private static void Shell(List<ShellWord> words, ShellReader reader)
    {
        var (index, command) = (1, false);

        while (index < words.Count && words[index].Value.Length > 1 && words[index].Value[0] is '-' or '+')
        {
            var option = words[index].Value;

            if (option.StartsWith("--", StringComparison.Ordinal))
            {
                reader.Unanalysable();

                return;
            }

            command |= option[0] == '-' && option.Contains('c', StringComparison.Ordinal);
            index += option[1..] is "o" or "O" ? 2 : 1;
        }

        if (command && (index >= words.Count || !words[index].Literal))
        {
            reader.Unanalysable();
        }
        else if (command)
        {
            reader.Merge(words[index].Value, opaque: false);
        }
    }
}
