using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Board;

internal enum JobGroup
{
    NeedsYou,
    Running,
    ReadyForReview,
    Done,
}

internal enum FactKind
{
    Starting,
    Working,
    PlanProgress,
    Verifying,
    AsksPermission,
    AsksQuestion,
    AsksPlanApproval,
    AsksForInput,
    Held,
    NeedsHelp,
    Verified,
    NoChecks,
    ReadyForReview,
    Approved,
    Discarded,
    Failed,
}

internal sealed record JobFact(FactKind Kind)
{
    public Option<ItemKind> Item { get; init; }

    public Option<HoldReason> Hold { get; init; }

    public int Attempt { get; init; }

    public int Done { get; init; }

    public int Total { get; init; }

    public Option<string> Strategy { get; init; }
}

internal static class JobFacts
{
    public static JobGroup GroupOf(BoardJob job) =>
        job.PendingDecisions > 0
            ? JobGroup.NeedsYou
            : job.Status switch
            {
                JobStatus.NeedsHelp => JobGroup.NeedsYou,
                JobStatus.AwaitingReview => JobGroup.ReadyForReview,
                JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed => JobGroup.Done,
                _ => JobGroup.Running,
            };

    public static JobFact FactOf(BoardJob job) =>
        job.Transcript.Awaiting.FirstOrDefault() switch
        {
            PermissionEntry permission => new JobFact(FactKind.AsksPermission) { Item = permission.Kind },
            FormEntry form => Asked(form),
            _ => Settled(job),
        };

    private static JobFact Asked(FormEntry form) => form.Form.Purpose switch
    {
        FormPurpose.Question => new JobFact(FactKind.AsksQuestion),
        FormPurpose.PlanApproval => new JobFact(FactKind.AsksPlanApproval),
        FormPurpose.Permission => new JobFact(FactKind.AsksPermission),
        _ => new JobFact(FactKind.AsksForInput),
    };

    private static JobFact Settled(BoardJob job) => job.Status switch
    {
        JobStatus.Running => job.Transcript.Plan.Match(
            plan => new JobFact(FactKind.PlanProgress) { Done = plan.Done, Total = plan.Total },
            () => new JobFact(FactKind.Working)),
        JobStatus.Checking => new JobFact(FactKind.Verifying) { Attempt = job.Attempts },
        JobStatus.NeedsHelp => job.Hold.Match(
            hold => new JobFact(FactKind.Held) { Hold = hold },
            () => new JobFact(FactKind.NeedsHelp)),
        JobStatus.AwaitingReview => job.Verification.Match(Reviewed, () => new JobFact(FactKind.ReadyForReview)),
        JobStatus.Approved => new JobFact(FactKind.Approved) { Strategy = job.Delivery.Map(delivery => delivery.Strategy) },
        JobStatus.Discarded => new JobFact(FactKind.Discarded),
        JobStatus.Failed => new JobFact(FactKind.Failed),
        _ => new JobFact(FactKind.Starting),
    };

    private static JobFact Reviewed(VerificationReport report) => report.Outcome switch
    {
        VerificationOutcome.Passed => new JobFact(FactKind.Verified) { Attempt = report.Attempt },
        VerificationOutcome.NoChecksDeclared => new JobFact(FactKind.NoChecks),
        _ => new JobFact(FactKind.ReadyForReview),
    };
}
