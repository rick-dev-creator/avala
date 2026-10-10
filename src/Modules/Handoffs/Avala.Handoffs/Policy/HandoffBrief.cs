using System.Globalization;
using System.Text;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Handoffs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Handoffs.Policy;

internal sealed record BriefFacts(string Instruction, ConnectionName From, ConnectionName To, Option<LimitReason> Why)
{
    public IReadOnlyList<PlanStep> Plan { get; init; } = [];

    public Option<IReadOnlyList<FileChange>> Files { get; init; }

    public Option<VerificationReport> Verification { get; init; }

    public Option<string> Feedback { get; init; }

    public IReadOnlyList<string> Decisions { get; init; } = [];

    public IReadOnlyList<string> Questions { get; init; } = [];

    public Option<string> LastMessage { get; init; }
}

internal static class HandoffBrief
{
    public const int LongestBrief = 16_000;

    public const int LongestMessage = 2_000;

    public const int LongestList = 20;

    public static string Compose(BriefFacts facts)
    {
        var brief = new StringBuilder()
            .AppendLine(Opening(facts))
            .AppendLine()
            .AppendLine("## The original instruction")
            .AppendLine(facts.Instruction)
            .AppendLine()
            .AppendLine("## The plan")
            .AppendLine(Lines(facts.Plan.Select(step => $"- [{Status(step.Status)}] {step.Title}"), "No plan was reported."))
            .AppendLine()
            .AppendLine("## Files changed so far")
            .AppendLine(facts.Files.Match(files => Lines(files.Select(File), "No file has changed yet."), () => "The changes could not be read."))
            .AppendLine()
            .AppendLine("## Checks")
            .AppendLine(Checks(facts))
            .AppendLine()
            .AppendLine("## Decisions taken")
            .AppendLine(Lines(facts.Decisions.Select(decision => $"- {decision}"), "None."))
            .AppendLine()
            .AppendLine("## Open questions")
            .AppendLine(Lines(facts.Questions.Select(question => $"- {question}"), "None."))
            .AppendLine()
            .AppendLine("## The last message of the previous agent")
            .AppendLine(facts.LastMessage.Match(Bounded, () => "None."))
            .AppendLine()
            .AppendLine("## What remains")
            .Append(Lines(Remaining(facts), "- Complete the original instruction."))
            .ToString();

        return brief.Length <= LongestBrief ? brief : $"{brief[..(LongestBrief - 5)]}[...]";
    }

    public static string Window(string window) =>
        window.Length == 0 ? "usage window"
        : window.Length > 1 && char.IsDigit(window[0]) && window[^1] is 'h' or 'd' && int.TryParse(window[..^1], CultureInfo.InvariantCulture, out var count)
            ? $"{count}-{(window[^1] == 'h' ? "hour" : "day")} window"
            : $"{window} window";

    public static string Percent(double fraction) => string.Create(CultureInfo.InvariantCulture, $"{fraction * 100:0}%");

    private static string Opening(BriefFacts facts) =>
        $"This job was handed off to you from {facts.From.Value}"
        + facts.Why.Match(why => $", which reached {Percent(why.Used)} of its {Window(why.Window)}", () => string.Empty)
        + $". You continue it on {facts.To.Value} in a new conversation. The worktree you are in already holds the work so far, checkpointed: continue it, do not start over. Everything below comes from Avala's own records of the job.";

    private static string Checks(BriefFacts facts)
    {
        IEnumerable<string> lines =
        [
            .. facts.Verification.Match<IEnumerable<string>>(
                report => [string.Create(CultureInfo.InvariantCulture, $"The last verification, of attempt {report.Attempt}: {Outcome(report.Outcome)}."), .. report.Checks.Select(Check)],
                () => ["No checks ran yet."]),
            .. facts.Feedback.Match<string[]>(feedback => [$"Feedback: {feedback}"], () => []),
        ];

        return string.Join(Environment.NewLine, lines);
    }

    private static IEnumerable<string> Remaining(BriefFacts facts)
    {
        var failing = facts.Verification.Match(
            report => report.Checks.Where(check => check.Status is not (CheckStatus.Passed or CheckStatus.Skipped)).Select(check => check.Name).ToList(),
            () => []);
        var pending = facts.Plan.Where(step => step.Status != PlanStepStatus.Done).Select(step => step.Title).ToList();

        return
        [
            .. failing.Count > 0 ? [$"- Make the failing checks pass: {string.Join(", ", failing)}."] : Array.Empty<string>(),
            .. pending.Count > 0 ? [$"- Finish the pending steps of the plan: {string.Join(", ", pending)}."] : Array.Empty<string>(),
        ];
    }

    private static string Lines(IEnumerable<string> items, string none)
    {
        var all = items.ToList();

        if (all.Count == 0)
        {
            return none;
        }

        var kept = all.TakeLast(LongestList).ToList();

        if (all.Count > kept.Count)
        {
            kept.Insert(0, string.Create(CultureInfo.InvariantCulture, $"({all.Count - kept.Count} earlier left out)"));
        }

        return string.Join(Environment.NewLine, kept);
    }

    private static string File(FileChange change) =>
        $"- {change.Path} ({change.Kind.ToString().ToLowerInvariant()}"
        + change.Added.Match(added => string.Create(CultureInfo.InvariantCulture, $", +{added}"), () => string.Empty)
        + change.Removed.Match(removed => string.Create(CultureInfo.InvariantCulture, $" -{removed}"), () => string.Empty)
        + ")";

    private static string Check(CheckEvidence check) =>
        $"- {check.Name}: {check.Status}"
        + check.ExitCode.Match(code => string.Create(CultureInfo.InvariantCulture, $", exit {code}"), () => string.Empty);

    private static string Outcome(VerificationOutcome outcome) => outcome switch
    {
        VerificationOutcome.Passed => "passed",
        VerificationOutcome.Failed => "failed",
        VerificationOutcome.NoChecksDeclared => "no checks declared",
        _ => "the checks file is invalid",
    };

    private static string Status(PlanStepStatus status) => status switch
    {
        PlanStepStatus.Done => "done",
        PlanStepStatus.InProgress => "in progress",
        _ => "pending",
    };

    private static string Bounded(string text) => text.Length <= LongestMessage ? text : $"{text[..LongestMessage]}[...]";
}
