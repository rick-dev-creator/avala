using System.Globalization;
using System.Text.RegularExpressions;
using Avala.Agents.Contracts.Events;
using Avala.CommandLines;

namespace Avala.Workbench.Presenting;

internal sealed record RunCommand(string Name, IReadOnlyList<string> Writes)
{
    public string Summary => Writes.Count == 0 ? Name : $"{Name} > {string.Join(", ", Writes)}";
}

internal static partial class CommandPhrases
{
    private const int LineWidth = 80;

    private const int ShownWidth = 56;

    private const int ListWidth = 120;

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

    public static string Title(ItemKind kind, string title, string command) =>
        kind == ItemKind.Command && IsLong(command) ? $"Run {Running(command)}" : title;

    public static string Running(string command)
    {
        var commands = IsLong(command) ? Commands(command) : [];

        return commands.Count > 1
            ? string.Create(CultureInfo.InvariantCulture, $"{commands.Count} commands: {Listed([.. commands.Select(found => found.Summary).Distinct(StringComparer.Ordinal)])}")
            : OneLine(command);
    }

    private static string Listed(IReadOnlyList<string> summaries)
    {
        var shown = summaries.Count;

        while (shown > 1 && string.Join(", ", summaries.Take(shown)).Length > ListWidth)
        {
            shown--;
        }

        return shown == summaries.Count ? string.Join(", ", summaries) : $"{string.Join(", ", summaries.Take(shown))}, …";
    }

    public static IReadOnlyList<string> Writes(ItemKind kind, string command) =>
        kind == ItemKind.Command ? [.. Read(command).Writes.Distinct(StringComparer.Ordinal)] : [];

    public static IReadOnlyList<RunCommand> Commands(string script) => [.. Read(script).Commands.Where(command => !command.Restated).SelectMany(Named)];

    private static CommandLine Read(string script) => CommandLine.Parse(script.ReplaceLineEndings("\n"));

    private static string[] Lines(string text) => text.Trim().ReplaceLineEndings("\n").Split('\n');

    private static RunCommand[] Named(ShellCommand command)
    {
        var name = command.Words.FirstOrDefault(word => !Prefixes.Contains(word) && !Assignment().IsMatch(word));

        return name is null || Closings.Contains(name) || Headers.Contains(name) ? [] : [new RunCommand(name, command.Writes)];
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*=")]
    private static partial Regex Assignment();
}
