using System.Collections.Frozen;
using Avala.Agents.Contracts.Events;
using Avala.CommandLines;
using Avala.Delegation.Contracts;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Reviewing;

internal sealed record ReportedRun(string Command, bool Passed);

internal static class ReportedRuns
{
    private static readonly FrozenSet<string> Inspecting = FrozenSet.Create(
        StringComparer.Ordinal,
        "ls", "cat", "head", "tail", "less", "more", "grep", "egrep", "fgrep", "rg", "find", "wc", "stat", "file", "tree", "sort", "uniq", "cut",
        "tr", "awk", "sed", "diff", "echo", "printf", "pwd", "which", "type", "git", "gh", "cd", "true", "false", "test", "[");

    private static readonly FrozenSet<string> Verifying = FrozenSet.Create(
        StringComparer.Ordinal,
        "test", "tests", "build", "check", "lint", "vet", "verify", "typecheck", "tsc", "pytest", "jest", "vitest", "mocha", "rspec", "clippy", "ctest", "phpunit", "unittest");

    private static readonly char[] Separators = [':', '='];

    private static readonly FrozenSet<string> Writing = FrozenSet.Create(StringComparer.Ordinal, "tee", "cp", "mv", "rm", "touch", "mkdir", "patch", "ln", "truncate", "dd");

    private static readonly FrozenSet<string> GitWriting = FrozenSet.Create(StringComparer.Ordinal, "apply", "checkout", "restore", "reset", "merge", "rebase", "cherry-pick", "stash", "pull", "switch", "mv", "rm");

    public static IReadOnlyList<ReportedRun> AfterLastEdit(IEnumerable<ITimelineEntry> entries)
    {
        var tools = Done(entries);
        var lastEdit = tools.FindLastIndex(Edits);

        return [.. tools.Skip(lastEdit + 1)
            .Where(tool => tool.Kind == ItemKind.Command && tool.Outcome.Match(outcome => outcome is ItemOutcome.Succeeded or ItemOutcome.Failed, () => false))
            .Select(tool => new ReportedRun(Command(tool), tool.Outcome == ItemOutcome.Succeeded))
            .Where(run => Verifies(run.Command))
            .GroupBy(run => run.Command, StringComparer.Ordinal)
            .Select(runs => runs.Last())];
    }

    public static bool Edited(IEnumerable<ITimelineEntry> entries) => Done(entries).Exists(Edits);

    private static List<ToolEntry> Done(IEnumerable<ITimelineEntry> entries) => [.. entries.OfType<ToolEntry>().Where(tool => !tool.Denied)];

    private static string Command(ToolEntry tool) => tool.Input.Match(input => input, () => tool.Title);

    private static bool Edits(ToolEntry tool) => tool.Kind switch
    {
        ItemKind.FileEdit or ItemKind.Subagent => true,
        ItemKind.Command => Writes(Command(tool)) && !Verifies(Command(tool)),
        _ => tool.Delegation.Match(Changed, () => false),
    };

    private static bool Changed(DelegationRecord delegation) =>
        delegation.Report.Match(report => report.Outcome == ChildOutcome.Integrated || report.Files.Count > 0, () => false);

    private static bool Writes(string command)
    {
        var line = CommandLine.Parse(command);

        return line.Opaque || line.Writes.Count > 0 || line.Commands.Any(simple => simple.Words switch
        {
            ["git", var verb, ..] => GitWriting.Contains(verb),
            ["sed" or "perl", ..] => simple.Words.Skip(1).Any(word => word.StartsWith("-i", StringComparison.Ordinal) || word.StartsWith("--in-place", StringComparison.Ordinal)),
            [var program, ..] => Writing.Contains(Path.GetFileName(program)),
            _ => false,
        });
    }

    private static bool Verifies(string command) =>
        CommandLine.Parse(command).Commands.Any(simple =>
            simple.Words.Count > 0
            && !Inspecting.Contains(Path.GetFileName(simple.Words[0]))
            && simple.Words.Any(word => Verifying.Contains(word.TrimStart('-').Split(Separators)[0].ToLowerInvariant())));
}
