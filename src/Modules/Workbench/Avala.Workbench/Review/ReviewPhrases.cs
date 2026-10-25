using ForgeError = Avala.Forges.Contracts.ForgeError;
using PullRequestDelivery = Avala.Forges.Contracts.PullRequestDelivery;
using PullRequestOffer = Avala.Forges.Contracts.PullRequestOffer;
using ForgeReadinessKind = Avala.Forges.Contracts.ForgeReadinessKind;
using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Presenting;
using Avala.Workbench.Reviewing;
using Avala.Workbench.Timeline;
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
    public const string ResubmitAdvice =
        "Fix .avala/checks.json and commit it, then resubmit the job. Continuing it would check it against the old file again: a job keeps the rules commit it was prepared with.";

    private const int TailLines = 12;

    private const string Keep = "keep";

    private const string Merge = "merge";

    public static string NotResubmitted(JobRejection rejection) => $"The job was not resubmitted. {NewJob.NewJobPhrases.Rejection(rejection)}";

    public const string FoundByAvala = "with the checks Avala found";

    public static string Verdict(ReviewVerdict verdict) => verdict.Kind switch
    {
        VerdictKind.Verified when verdict.Discovered => string.Create(CultureInfo.InvariantCulture, $"Verified on attempt {verdict.Attempt} of {verdict.Attempts} {FoundByAvala}"),
        VerdictKind.Failed when verdict.Discovered => string.Create(CultureInfo.InvariantCulture, $"Not verified: attempt {verdict.Attempt} of {verdict.Attempts} failed the checks Avala found"),
        VerdictKind.Verified => string.Create(CultureInfo.InvariantCulture, $"Verified on attempt {verdict.Attempt} of {verdict.Attempts}"),
        VerdictKind.NoChecks => "No checks declared: nothing proves the work",
        VerdictKind.Failed => string.Create(CultureInfo.InvariantCulture, $"Not verified: attempt {verdict.Attempt} of {verdict.Attempts} failed its checks"),
        VerdictKind.InvalidDeclaration => "Not verified: the check declaration is invalid",
        _ => "Not verified",
    };

    public static string Plan(Option<PlanEntry> plan) =>
        plan.Match(
            entry => entry.Total == 0 ? string.Empty
                : entry.Done == entry.Total ? string.Create(CultureInfo.InvariantCulture, $"Plan · all {entry.Total} {Steps(entry.Total)} done")
                : string.Create(CultureInfo.InvariantCulture, $"Plan · {entry.Done} of {entry.Total} {Steps(entry.Total)} done"),
            () => string.Empty);

    private static string Steps(int count) => count == 1 ? "step" : "steps";

    public const string MissingChecks =
        "This repository has no .avala/checks.json, so Avala ran nothing to verify the work. Checks declared and committed verify the jobs that start after them.";

    public const string EmptyChecks =
        "This repository's .avala/checks.json declares no checks, so Avala ran nothing to verify the work. Checks declared and committed verify the jobs that start after them.";

    public static string ReportedTitle(bool afterEdit) => afterEdit ? "Reported by the agent after its last edit" : "Reported by the agent";

    public static string Lacks(VerificationReport last) => last.Missing ? MissingChecks : EmptyChecks;

    public static string Proof(RunEvidence evidence) =>
        evidence.Verifications.Count == 0
            ? "No checks ran in the worktree."
            : Proof(evidence.Verifications[^1]);

    public static string Proof(RunEvidence evidence, Option<BoardJob> job) =>
        job.Match(
            found => Proof(evidence, ReportedRuns.AfterLastEdit(found.Transcript.Entries), ReportedRuns.Edited(found.Transcript.Entries)),
            () => Proof(evidence));

    public static string Proof(RunEvidence evidence, IReadOnlyList<ReportedRun> reported, bool afterEdit) =>
        reported.Count > 0 && evidence.Verifications is [.., { Outcome: VerificationOutcome.NoChecksDeclared }]
            ? $"The repository declares no checks. {(afterEdit ? "After its last edit the agent" : "The agent")} reported {Names(reported.Select(run => $"{CommandPhrases.OneLine(run.Command)} {Outcome(run)}"))}."
            : Proof(evidence);

    public const string Shipped = "Avala found these checks by running the repository's own commands, and .avala/checks.json ships them with this job.";

    private static string Proof(VerificationReport last) => last.Discovered && last.DiscoveryShipped ? $"{Proved(last)} {Shipped}" : Proved(last);

    private static string Proved(VerificationReport last) => last.Outcome switch
    {
        VerificationOutcome.Passed => $"{Names(last.Checks.Select(check => check.Name))} {(last.Checks.Count > 1 ? "all passed" : "passed")} in the worktree after the last turn.",
        VerificationOutcome.Failed => $"{Names(last.Checks.Where(check => check.Status != CheckStatus.Passed && check.Status != CheckStatus.Skipped).Select(check => check.Name))} failed on the last attempt.",
        VerificationOutcome.NoChecksDeclared => "The repository declares no checks, so nothing ran.",
        _ => "The repository's check declaration could not be read.",
    };

    public static string Ran(ReportedRun run) => $"{CommandPhrases.OneLine(run.Command)} · {Outcome(run)}";

    private static string Outcome(ReportedRun run) => run.Masked ? "ran, its result hidden by what followed it" : run.Passed ? "passed" : "failed";

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
        FailedAttempt failed => Failed(failed),
        PolicyDenial denial => Denied(denial.Decision),
        HumanDenial denial => Denied(denial.Answer),
        DeclinedForm declined => new($"Declined: {declined.Form.Form.Title}", "form", string.Empty, string.Empty),
        MadeAssumption assumed => Assumed(assumed.Assumption),
        ContinuedAfterHold held => Continued(held.Attempt),
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

    public static string Delivered(ApprovalDelivery delivery, bool documented) =>
        delivery.Strategy == Keep && documented ? $"{Delivered(delivery)} Its docs stay on that branch until you merge it." : Delivered(delivery);

    public static string Delivered(ApprovalDelivery delivery) =>
        delivery.Strategy == PullRequestDelivery.Strategy
            ? $"Pushed {delivery.Branch} and opened its pull request; the inspector follows its checks"
            : delivery.Commit.Match(
            commit => $"Merged into {delivery.Branch} as {commit[..Math.Min(7, commit.Length)]}.",
            () => $"Branch {delivery.Branch} kept. Merge it when you are ready.");

    public static string Approve(DeliveryFacts facts) =>
        facts.Plan.Match(
            plan => plan switch
            {
                { IntoParent: true } => "Merge into the parent job",
                { Strategy: Merge } => plan.Base.Match(target => $"Merge into {target}", () => "Merge"),
                { Strategy: PullRequestDelivery.Strategy } => facts.PullRequest.Match(offer => $"Open pull request on {offer.Forge.Value}", () => "Open pull request"),
                { Strategy: Keep } => $"Keep branch {Short(plan.Branch)}",
                _ => $"Approve through {plan.Strategy}",
            },
            () => "Approve");

    public static string Short(string branch)
    {
        var slash = branch.LastIndexOf('/');
        var name = branch[(slash + 1)..];

        return name.Length > 12 ? $"{branch[..(slash + 1)]}{name[..8]}" : branch;
    }

    public static string PullRequestAction(DeliveryFacts facts) =>
        facts.PullRequest.Match(
            offer => facts.Plan.Match(plan => plan.IntoParent || plan.Strategy == PullRequestDelivery.Strategy, () => false) ? string.Empty : $"Open pull request on {offer.Forge.Value}",
            () => string.Empty);

    public static bool PullRequestFirst(DeliveryFacts facts) =>
        facts.PullRequest.IsSome && facts.Plan.Match(plan => !plan.IntoParent && plan.Strategy == Keep, () => false);

    public static string ForgeGap(DeliveryFacts facts) =>
        facts.Forge.Match(
            forge => forge.Kind is ForgeReadinessKind.Connectable or ForgeReadinessKind.SignInNeeded ? $"Connect {forge.Host} in Settings to open and follow pull requests" : string.Empty,
            () => string.Empty);

    public static string Conflicted(IReadOnlyList<string> conflicts) =>
        conflicts.Count > 0
            ? $"The work conflicts with the base branch in {string.Join(", ", conflicts)}."
            : Refusal(JobRejection.MergeConflict);

    public static string Offer(PullRequestOffer offer) => $"Open pull request on {offer.Forge.Value} ({offer.ForgeKind}, {offer.Remote})";

    public static string Refusal(JobRejection rejection, Option<ForgeError> forge) =>
        forge.Match(error => $"The pull request was not opened: {ForgePhrases.Error(error)} The job still awaits review.", () => Refusal(rejection));

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

    private static ExceptionPhrase Failed(FailedAttempt failed) => new(
        string.Create(CultureInfo.InvariantCulture, $"Attempt {failed.Attempt} failed"),
        string.Join(", ", failed.Checks.Select(Check)),
        string.Empty,
        string.Join("\n", failed.Checks.Select(Tail).Where(tail => tail.Length > 0)));

    private static ExceptionPhrase Denied(PolicyDecision decision) => new(
        $"Denied: {CommandPhrases.Request(decision.Kind, decision.Target)}",
        decision.Rule.Match(rule => $"rule {rule.Name}", () => "default policy"),
        decision.Rule.Match(rule => $"Denied by the rule {rule.Name}.", () => "Denied by the default policy."),
        decision.Target);

    private static ExceptionPhrase Denied(HumanAnswer answer) => new(
        $"You denied: {CommandPhrases.Request(answer.Kind, answer.Target)}",
        "by you",
        answer.Message.Match(message => message, () => string.Empty),
        answer.Target);

    private static ExceptionPhrase Continued(AttemptRecord attempt) => new(
        string.Create(CultureInfo.InvariantCulture, $"Held, then continued on attempt {attempt.Number}"),
        "held",
        attempt.Guidance.Match(guidance => guidance, () => string.Empty),
        string.Empty);

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
}
