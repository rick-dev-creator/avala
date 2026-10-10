using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workbench.Conversation;
using Avala.Workbench.Presenting;
using Avala.Workbench.Reviewing;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Review;

internal enum ExceptionTone
{
    Neutral,
    Attention,
    Failure,
}

internal sealed record ExceptionPhrase(string Title, string Fact, string Detail, string Output);

internal static class ReviewPhrases
{
    private const int TailLines = 12;

    public static string Verdict(ReviewVerdict verdict) => verdict.Kind switch
    {
        VerdictKind.Verified => string.Create(CultureInfo.InvariantCulture, $"Verified on attempt {verdict.Attempt} of {verdict.Attempts}"),
        VerdictKind.NoChecks => "No checks declared: nothing proves the work",
        VerdictKind.Failed => string.Create(CultureInfo.InvariantCulture, $"Not verified: attempt {verdict.Attempt} of {verdict.Attempts} failed its checks"),
        VerdictKind.InvalidDeclaration => "Not verified: the check declaration is invalid",
        _ => "Not verified",
    };

    public static string Proof(RunEvidence evidence) =>
        evidence.Verifications.Count == 0
            ? "No checks ran in the worktree."
            : Proof(evidence.Verifications[^1]);

    public static string Heading(JobStatus status) => status switch
    {
        JobStatus.AwaitingReview => "Ready for review",
        JobStatus.NeedsHelp => "Needs help",
        JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed or JobStatus.Running or JobStatus.Checking => status.ToString(),
        _ => "Not ready for review",
    };

    public static string Facts(JobHistory history) =>
        Amounts.Joined([
            Repository(history.Summary.Repository),
            .. history.Summary.Connection.Match<string[]>(connection => [connection.Value], () => []),
            .. history.Summary.Autonomy.Match<string[]>(autonomy => [autonomy.ToString()], () => []),
            Amounts.Count(history.Sessions.Count, "session", "sessions"),
        ]);

    public static ExceptionPhrase Exception(IReviewException exception) => exception switch
    {
        FailedAttempt failed => new(
            string.Create(CultureInfo.InvariantCulture, $"Attempt {failed.Attempt} failed"),
            string.Join(", ", failed.Checks.Select(Check)),
            string.Empty,
            string.Join("\n", failed.Checks.Select(Tail).Where(tail => tail.Length > 0))),
        PolicyDenial denial => new(
            $"Denied: {Request(denial.Decision.Kind, denial.Decision.Target)}",
            denial.Decision.Rule.Match(rule => $"rule {rule.Name}", () => "default policy"),
            denial.Decision.Rule.Match(rule => $"Denied by the rule {rule.Name}.", () => "Denied by the default policy."),
            denial.Decision.Target),
        HumanDenial denial => new(
            $"You denied: {Request(denial.Answer.Kind, denial.Answer.Target)}",
            "by you",
            denial.Answer.Message.Match(message => message, () => string.Empty),
            denial.Answer.Target),
        DeclinedForm declined => new($"Declined: {declined.Form.Form.Title}", "form", string.Empty, string.Empty),
        MadeAssumption assumed => Assumed(assumed.Assumption),
        ContinuedAfterHold held => new(
            string.Create(CultureInfo.InvariantCulture, $"Held, then continued on attempt {held.Attempt.Number}"),
            "held",
            held.Attempt.Guidance.Match(guidance => guidance, () => string.Empty),
            string.Empty),
        EditedRuleFile edited => new("The agent edited a rule file", edited.Path, "Its rules apply from the base commit, not from this edit.", string.Empty),
        _ => new("The diff could not be read", "rule files unproven", "Nothing proves the rule files are untouched.", string.Empty),
    };

    public static ExceptionTone Tone(IReviewException exception) => exception switch
    {
        FailedAttempt => ExceptionTone.Failure,
        PolicyDenial or HumanDenial or DeclinedForm or MadeAssumption => ExceptionTone.Neutral,
        _ => ExceptionTone.Attention,
    };

    public static string Quiet(RunEvidence evidence, Option<UsageSummary> usage) =>
        Amounts.Joined([
            Amounts.Count(evidence.AllowedByRules, "other decision was allowed by rules", "other decisions were allowed by rules"),
            .. usage.Match(Amounts.Spent, () => []),
        ]);

    public static string Changes(Result<WorkspaceDiff, WorkspaceFailure> diff) =>
        diff.Match(
            found => found.Files.Count == 0 ? "No files changed" : Amounts.Count(found.Files.Count, "file changed", "files changed"),
            failure => failure == WorkspaceFailure.UnknownWorkspace ? "The job has no worktree" : "The diff could not be read");

    public static string Totals(Result<WorkspaceDiff, WorkspaceFailure> diff) =>
        diff.Match(
            found => found.Files.Count == 0
                ? string.Empty
                : Lines(
                    found.Files.Sum(file => file.Added.Match(added => added, () => 0)),
                    found.Files.Sum(file => file.Removed.Match(removed => removed, () => 0))),
            _ => string.Empty);

    public static string Counts(FileChange file) =>
        file.Added.Match(added => Lines(added, file.Removed.Match(removed => removed, () => 0)), () => "binary");

    public static string Delivered(ApprovalDelivery delivery) =>
        delivery.Commit.Match(
            commit => $"Merged into {delivery.Branch} as {commit[..Math.Min(7, commit.Length)]}",
            () => $"Approved: the branch {delivery.Branch} is ready");

    public static string Conflicted(IReadOnlyList<string> conflicts) =>
        conflicts.Count > 0
            ? $"The work conflicts with the base branch in {string.Join(", ", conflicts)}."
            : Refusal(JobRejection.MergeConflict);

    public static string Refusal(JobRejection rejection) => rejection switch
    {
        JobRejection.MergeConflict => "The work conflicts with the base branch.",
        JobRejection.BaseCheckoutDirty => "The base branch's checkout has uncommitted changes. Commit or stash them, then approve again.",
        JobRejection.BaseMoved => "The base branch moved while merging. Approve again.",
        JobRejection.NoBaseBranch => "The job's base is not a branch, so there is nothing to merge into.",
        JobRejection.InvalidJobFile or JobRejection.UnknownApprovalStrategy => "The repository's .avala/jobs.json names no usable approval strategy.",
        JobRejection.DeliveryFailed => "The delivery failed. The job still awaits review.",
        JobRejection.ParentNotRunning => "The parent job no longer runs, so its child cannot be integrated.",
        _ => ConversationPhrases.Rejection(rejection),
    };

    private static string Proof(VerificationReport last) => last.Outcome switch
    {
        VerificationOutcome.Passed => $"{Names(last.Checks.Select(check => check.Name))} {(last.Checks.Count > 1 ? "all passed" : "passed")} in the worktree after the last turn.",
        VerificationOutcome.Failed => $"{Names(last.Checks.Where(check => check.Status != CheckStatus.Passed && check.Status != CheckStatus.Skipped).Select(check => check.Name))} failed on the last attempt.",
        VerificationOutcome.NoChecksDeclared => "The repository declares no checks, so nothing ran.",
        _ => "The repository's check declaration could not be read.",
    };

    private static string Names(IEnumerable<string> names) =>
        names.ToList() switch
        {
            [] => "The checks",
            [var one] => one,
            [.. var first, var last] => $"{string.Join(", ", first)} and {last}",
        };

    private static string Repository(string repository) =>
        repository.Replace('\\', '/').TrimEnd('/').Split('/')[^1] is { Length: > 0 } name ? name : repository;

    private static string Lines(int added, int removed) =>
        (added, removed) switch
        {
            (_, 0) => string.Create(CultureInfo.InvariantCulture, $"+{added}"),
            (0, _) => string.Create(CultureInfo.InvariantCulture, $"−{removed}"),
            _ => string.Create(CultureInfo.InvariantCulture, $"+{added} −{removed}"),
        };

    private static ExceptionPhrase Assumed(Assumption assumption) => new(
        $"Assumed {(assumption.Chosen.Count > 0 ? string.Join(", ", assumption.Chosen) : "the agent's judgment")} for \"{assumption.Prompt}\"",
        assumption.Basis switch
        {
            AssumptionBasis.RecommendedOption => "recommended option",
            AssumptionBasis.FirstOption => "first option",
            AssumptionBasis.AgentJudgment => "agent's judgment",
            _ => "confirmed",
        },
        assumption.Basis switch
        {
            AssumptionBasis.RecommendedOption => "The policy took the recommended option.",
            AssumptionBasis.FirstOption => "The policy took the first option.",
            AssumptionBasis.AgentJudgment => "The agent was told to decide.",
            _ => "The policy confirmed it.",
        },
        string.Empty);

    private static string Check(CheckEvidence check) =>
        check.ExitCode.Match(
            code => string.Create(CultureInfo.InvariantCulture, $"{check.Name} · exit {code}"),
            () => check.Status == CheckStatus.TimedOut ? $"{check.Name} · timed out" : $"{check.Name} · not found");

    private static string Tail(CheckEvidence check) =>
        string.Join("\n", $"{check.OutputTail}\n{check.ErrorTail}".Trim().Split('\n').TakeLast(TailLines));

    private static string Request(ItemKind kind, string target) => kind switch
    {
        ItemKind.Command => $"run {CommandPhrases.Running(target)}",
        ItemKind.FileEdit => $"edit {CommandPhrases.OneLine(target)}",
        ItemKind.Web => $"reach {CommandPhrases.OneLine(target)}",
        _ => $"use {CommandPhrases.OneLine(target)}",
    };
}
