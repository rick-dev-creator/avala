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
        "tr", "awk", "sed", "diff", "echo", "printf", "pwd", "which", "type", "git", "gh", "tea", "cd", "true", "false", "test", "[");

    private static readonly FrozenSet<string> Verifying = FrozenSet.Create(
        StringComparer.Ordinal,
        "test", "tests", "build", "check", "lint", "vet", "verify", "typecheck", "tsc", "pytest", "jest", "vitest", "mocha", "rspec", "clippy", "ctest", "phpunit", "unittest");

    private static readonly FrozenSet<string> Writing = FrozenSet.Create(
        StringComparer.Ordinal, "cp", "mv", "rm", "rmdir", "touch", "mkdir", "patch", "ln", "truncate", "dd", "install", "gofmt", "rustfmt", "black", "isort", "autopep8");

    private static readonly FrozenSet<string> GitWriting = FrozenSet.Create(
        StringComparer.Ordinal, "apply", "am", "checkout", "restore", "reset", "revert", "clean", "merge", "rebase", "cherry-pick", "stash", "pull", "switch", "mv", "rm");

    private static readonly FrozenSet<string> Rewriting = FrozenSet.Create(
        StringComparer.Ordinal,
        "dotnet format", "cargo fmt", "cargo fix", "go fmt", "go generate", "go mod", "ruff format", "npm install", "npm i", "npm ci", "npm update", "yarn add", "yarn install",
        "pnpm install", "pnpm add", "bundle install", "pip install", "composer install");

    private static readonly char[] Separators = [':', '='];

    public static IReadOnlyList<ReportedRun> AfterLastEdit(IEnumerable<ITimelineEntry> entries)
    {
        var tools = Done(entries);
        var lastEdit = tools.FindLastIndex(Edits);

        return [.. tools.Skip(lastEdit + 1)
            .Where(tool => tool.Kind == ItemKind.Command && tool.Outcome.Match(outcome => outcome is ItemOutcome.Succeeded or ItemOutcome.Failed, () => false))
            .Select(tool => new ReportedRun(Command(tool), tool.Outcome == ItemOutcome.Succeeded))
            .Where(run => CommandLine.Parse(run.Command).Commands.Any(Verifies))
            .GroupBy(run => run.Command, StringComparer.Ordinal)
            .Select(runs => runs.Last())];
    }

    public static bool Edited(IEnumerable<ITimelineEntry> entries) => Done(entries).Exists(Edits);

    private static List<ToolEntry> Done(IEnumerable<ITimelineEntry> entries) => [.. entries.OfType<ToolEntry>().Where(tool => !tool.Denied)];

    private static string Command(ToolEntry tool) => tool.Input.Match(input => input, () => tool.Title);

    private static bool Edits(ToolEntry tool) => tool.Kind switch
    {
        ItemKind.FileEdit or ItemKind.Subagent => true,
        ItemKind.Command => Writes(CommandLine.Parse(Command(tool))),
        _ => tool.Delegation.Match(Changed, () => false),
    };

    private static bool Changed(DelegationRecord delegation) =>
        delegation.Report.Match(report => report.Outcome == ChildOutcome.Integrated || report.Files.Count > 0, () => false);

    private static bool Writes(CommandLine line) =>
        line.Opaque
        || line.Commands.Any(Rewrites)
        || ((line.Writes.Count > 0 || line.Commands.Any(Tees)) && !line.Commands.Any(Verifies));

    private static bool Rewrites(ShellCommand simple) => simple.Words switch
    {
        [] => false,
        ["git", var verb, ..] => GitWriting.Contains(verb),
        ["sed" or "perl", ..] => simple.Words.Skip(1).Any(word => word.StartsWith("-i", StringComparison.Ordinal) || word.StartsWith("--in-place", StringComparison.Ordinal)),
        [var program, ..] => Writing.Contains(Path.GetFileName(program))
            || (simple.Words.Count > 1 && Rewriting.Contains($"{Path.GetFileName(program)} {simple.Words[1]}"))
            || (simple.Words.Contains("prettier") && simple.Words.Any(word => word is "--write" or "-w"))
            || (simple.Words.Any(word => word.EndsWith("eslint", StringComparison.Ordinal)) && simple.Words.Contains("--fix")),
    };

    private static bool Tees(ShellCommand simple) => simple.Words is [var program, ..] && Path.GetFileName(program) == "tee";

    private static bool Verifies(ShellCommand simple)
    {
        if (simple.Words.Count == 0 || Rewrites(simple) || Tees(simple) || Inspecting.Contains(Path.GetFileName(simple.Words[0])))
        {
            return false;
        }

        var named = simple.Words.Take(3).Where(word => !word.StartsWith('-')).Select(word => Path.GetFileName(word));
        var flagged = simple.Words.Where(word => word.StartsWith("--", StringComparison.Ordinal)).Select(word => word.TrimStart('-'));

        return named.Concat(flagged).Any(word => Verifying.Contains(word.Split(Separators)[0].ToLowerInvariant()));
    }
}
