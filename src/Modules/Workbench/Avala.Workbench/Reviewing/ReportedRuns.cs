using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Avala.Agents.Contracts.Events;
using Avala.CommandLines;
using Avala.Delegation.Contracts;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Reviewing;

internal sealed record ReportedRun(string Command, bool Passed)
{
    public bool Masked { get; init; }
}

internal static partial class ReportedRuns
{
    private const string RunPrefix = "Run ";

    private static readonly FrozenSet<string> Checkers = FrozenSet.Create(
        StringComparer.Ordinal,
        "pytest", "jest", "vitest", "mocha", "rspec", "tsc", "ctest", "phpunit", "tox", "nox", "mypy", "pylint", "flake8", "eslint", "shellcheck", "golangci-lint");

    private static readonly FrozenSet<string> Runners = FrozenSet.Create(
        StringComparer.Ordinal,
        "dotnet", "cargo", "go", "npm", "yarn", "pnpm", "bun", "deno", "make", "just", "gradle", "gradlew", "mvn", "mvnw", "mix", "swift", "zig", "python", "python3",
        "npx", "bundle", "rake", "cmake", "node", "flutter", "poetry", "uv", "composer", "sbt", "lein", "stack", "cabal", "ruff", "nx", "turbo");

    private static readonly FrozenSet<string> Scripts = FrozenSet.Create(StringComparer.Ordinal, "run", "run-script", "exec", "x", "-m");

    private static readonly FrozenSet<string> Verifying = FrozenSet.Create(
        StringComparer.Ordinal,
        "test", "tests", "build", "check", "lint", "vet", "verify", "typecheck", "tsc", "pytest", "jest", "vitest", "mocha", "rspec", "clippy", "ctest", "phpunit", "unittest");

    private static readonly FrozenSet<string> Asking = FrozenSet.Create(StringComparer.Ordinal, "--version", "--help", "--list-tests", "--collect-only", "--co", "--listTests", "--list");

    private static readonly FrozenSet<string> Formatting = FrozenSet.Create(StringComparer.Ordinal, "format", "fmt", "prettier", "fix", "codegen", "generate");

    private static readonly FrozenSet<string> Writing = FrozenSet.Create(
        StringComparer.Ordinal,
        "cp", "mv", "rm", "rmdir", "touch", "mkdir", "patch", "ln", "truncate", "dd", "install", "gofmt", "goimports", "rustfmt", "black", "isort", "autopep8");

    private static readonly FrozenSet<string> InPlaceEditors = FrozenSet.Create(StringComparer.Ordinal, "sed", "perl", "clang-format");

    private static readonly FrozenSet<string> GitWriting = FrozenSet.Create(
        StringComparer.Ordinal, "apply", "am", "checkout", "restore", "reset", "revert", "clean", "merge", "rebase", "cherry-pick", "stash", "pull", "switch", "mv", "rm");

    private static readonly FrozenSet<string> GitValued = FrozenSet.Create(StringComparer.Ordinal, "-C", "-c", "--git-dir", "--work-tree", "--namespace");

    private static readonly FrozenSet<string> Rewriting = FrozenSet.Create(
        StringComparer.Ordinal,
        "dotnet format", "dotnet add", "dotnet remove", "cargo fmt", "cargo fix", "cargo add", "cargo remove", "go fmt", "go generate", "go mod", "go get", "ruff format",
        "npm install", "npm i", "npm ci", "npm update", "npm uninstall", "yarn add", "yarn install", "yarn remove", "pnpm install", "pnpm add", "pnpm remove",
        "bundle install", "bundle add", "bundle update", "pip install", "pip3 install", "pip uninstall", "uv add", "uv pip", "uv sync", "uv lock", "poetry add", "poetry install",
        "poetry lock", "composer install", "composer require", "composer update", "pnpm i", "pnpm update", "yarn upgrade", "cargo update", "dotnet restore", "go install");

    private static readonly FrozenSet<string> Wrappers = FrozenSet.Create(StringComparer.Ordinal, "env", "nohup", "time", "command", "nice", "stdbuf", "timeout");

    private static readonly FrozenSet<string> Logs = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, ".log", ".txt", ".out", ".trx");

    private static readonly char[] Separators = [':', '='];

    private static readonly char[] Parts = [':', '-', '_'];

    private static readonly FrozenSet<string> Fixing = FrozenSet.Create(
        StringComparer.Ordinal, "--fix", "--write", "--apply", "--updateSnapshot", "--update-snapshots", "--autocorrect", "--autocorrect-all", "--auto-correct", "--auto-correct-all");

    private static readonly FrozenSet<string> Interpreters = FrozenSet.Create(StringComparer.Ordinal, "python", "python3", "node", "ruby", "perl", "php", "bun", "deno", "Rscript", "pwsh");

    private static readonly FrozenSet<string> Inline = FrozenSet.Create(StringComparer.Ordinal, "-c", "-e", "-E", "-r", "-", "eval", "--eval", "-Command");

    public static IReadOnlyList<ReportedRun> AfterLastEdit(IEnumerable<ITimelineEntry> entries)
    {
        var tools = Done(entries);
        var lastEdit = tools.FindLastIndex(Edits);

        return [.. tools.Skip(lastEdit + 1)
            .Where(tool => tool.Kind == ItemKind.Command && tool.Outcome.Match(outcome => outcome is ItemOutcome.Succeeded or ItemOutcome.Failed, () => false))
            .Select(tool => new ReportedRun(Command(tool), tool.Outcome == ItemOutcome.Succeeded) { Masked = Masked(Command(tool)) })
            .Where(run => CommandLine.Parse(run.Command).Commands.Any(Verifies))
            .GroupBy(run => run.Command, StringComparer.Ordinal)
            .Select(runs => runs.Last())];
    }

    public static bool Edited(IEnumerable<ITimelineEntry> entries) => Done(entries).Exists(Edits);

    private static bool Masked(string command)
    {
        var line = CommandLine.Parse(command);
        var last = line.Commands.LastOrDefault(simple => !simple.Restated);

        return last is not null && !Verifies(last) && Sequenced(command);
    }

    private static bool Sequenced(string command)
    {
        var bare = Quoted().Replace(command, string.Empty).Replace("&&", " ", StringComparison.Ordinal).Replace(">&", ">", StringComparison.Ordinal).Replace("&>", ">", StringComparison.Ordinal);

        return !bare.Contains("pipefail", StringComparison.Ordinal) && bare.IndexOfAny([';', '|', '&', '\n']) >= 0;
    }

    private static List<ToolEntry> Done(IEnumerable<ITimelineEntry> entries) => [.. entries.OfType<ToolEntry>().Where(tool => !tool.Denied)];

    private static string Command(ToolEntry tool) =>
        tool.Input.Match(input => input, () => tool.Title.StartsWith(RunPrefix, StringComparison.Ordinal) ? tool.Title[RunPrefix.Length..] : tool.Title);

    private static bool Edits(ToolEntry tool) => tool.Kind switch
    {
        ItemKind.FileEdit or ItemKind.Subagent => true,
        ItemKind.Command => Writes(CommandLine.Parse(Command(tool))),
        _ => tool.Delegation.Match(Changed, () => false),
    };

    private static bool Changed(DelegationRecord delegation) =>
        delegation.Report.Match(report => report.Outcome == ChildOutcome.Integrated || report.Files.Count > 0, () => false);

    private static bool Writes(CommandLine line)
    {
        var verifies = line.Commands.Any(Verifies);

        return line.Opaque
            || line.Commands.Any(simple => Rewrites(simple)
                || (simple.Writes.Count > 0 && !(Verifies(simple) && simple.Writes.All(Logged)))
                || (Tees(simple) && !(verifies && Teed(simple).All(Logged))));
    }

    private static bool Logged(string path) => Logs.Contains(Path.GetExtension(path));

    private static IEnumerable<string> Teed(ShellCommand simple) => Unwrapped(simple).Skip(1).Where(word => !word.StartsWith('-'));

    private static bool Rewrites(ShellCommand simple)
    {
        var words = Unwrapped(simple);

        return words switch
        {
            [] => false,
            ["git", ..] => GitVerb(words).Match(GitWriting.Contains, () => false),
            [var program, ..] when InPlaceEditors.Contains(Program(program)) => words.Skip(1).Any(InPlace),
            [var program, ..] => Writing.Contains(Program(program))
                || words.Zip(words.Skip(1)).Any(pair => Rewriting.Contains($"{Program(pair.First)} {pair.Second}"))
                || Interprets(words)
                || Formats(Program(program), words)
                || Fixes(words),
        };
    }

    private static bool Formats(string program, List<string> words) =>
        Runners.Contains(program) && Named(words).Take(2).SelectMany(word => word.Split(Parts)).Any(Formatting.Contains);

    private static bool Fixes(List<string> words) =>
        words.Any(word => Fixing.Contains(word) || word.StartsWith("--fix=", StringComparison.Ordinal))
        || (words.Any(word => word is "rubocop" or "jest" or "vitest") && words.Any(word => word is "-a" or "-A" or "-u"))
        || (words.Contains("prettier") && words.Contains("-w"));

    private static bool Interprets(List<string> words) => Interpreters.Contains(Program(words[0])) && words.Skip(1).Any(Inline.Contains);

    private static Sdk.Option<string> GitVerb(List<string> words)
    {
        var valued = false;

        foreach (var word in words.Skip(1))
        {
            if (!valued && !word.StartsWith('-'))
            {
                return word;
            }

            valued = !valued && GitValued.Contains(word);
        }

        return Sdk.Option<string>.None;
    }

    private static bool InPlace(string word) =>
        word.StartsWith("--in-place", StringComparison.Ordinal)
        || (word.Length > 1 && word[0] == '-' && word[1] != '-' && word.Contains('i', StringComparison.Ordinal));

    private static bool Tees(ShellCommand simple) => Unwrapped(simple) is [var program, ..] && Program(program) == "tee";

    private static bool Verifies(ShellCommand simple)
    {
        var words = Unwrapped(simple);

        if (words.Count == 0 || Rewrites(simple) || words.Any(Asking.Contains))
        {
            return false;
        }

        var program = Program(words[0]);

        if (Checkers.Contains(program))
        {
            return true;
        }

        var named = Named(words).ToList();
        var task = named is [var first, var second, ..] && Scripts.Contains(first) ? second : named.FirstOrDefault() ?? string.Empty;
        var flagged = words.Where(word => word.StartsWith("--", StringComparison.Ordinal)).Select(word => word.TrimStart('-'));

        return Runners.Contains(program) && flagged.Append(Path.GetFileName(task)).Any(word => Verifying.Contains(word.Split(Separators)[0].ToLowerInvariant()));
    }

    private static IEnumerable<string> Named(IReadOnlyList<string> words) =>
        words.Skip(1).Where(word => !word.StartsWith('-') || word == "-m");

    private static List<string> Unwrapped(ShellCommand simple)
    {
        var words = simple.Words.ToList();

        while (words.Count > 0 && (Assigns(words[0]) || Wrappers.Contains(Program(words[0]))))
        {
            var wrapper = Program(words[0]);
            words.RemoveAt(0);

            while (words.Count > 0 && (words[0].StartsWith('-') || (wrapper == "env" && Assigns(words[0]))))
            {
                words.RemoveAt(0);
            }

            if (wrapper == "timeout" && words.Count > 0)
            {
                words.RemoveAt(0);
            }
        }

        return words;
    }

    private static bool Assigns(string word) =>
        word.IndexOf('=', StringComparison.Ordinal) is var equals and > 0 && word[..equals].All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static string Program(string word) => Path.GetFileName(word);

    [GeneratedRegex(@"'[^']*'|""(?:\\.|[^""\\])*""")]
    private static partial Regex Quoted();
}
