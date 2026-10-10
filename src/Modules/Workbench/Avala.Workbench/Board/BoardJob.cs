using Avala.Forges.Contracts;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Board;

internal sealed record BoardJob(JobSummary Summary, Transcript Transcript)
{
    public JobId Job => Summary.Job;

    public JobStatus Status => Summary.Status;

    public int Attempts { get; init; }

    public Option<HoldReason> Hold { get; init; }

    public Option<VerificationReport> Verification { get; init; }

    public Option<ApprovalDelivery> Delivery { get; init; }

    public Option<ConnectionChoice> Choice { get; init; }

    public IReadOnlyList<HandoffRecord> Handoffs { get; init; } = [];

    public Option<ResetWait> Wait { get; init; }

    public Option<PullRequestWatchState> PullRequest { get; init; }

    public IReadOnlyList<WakeUpRecord> WakeUps { get; init; } = [];

    public int Revision { get; init; }

    public bool TakesMessagesMidTurn { get; init; }

    public int PendingDecisions => Transcript.Awaiting.Count();

    public JobGroup Group => JobFacts.GroupOf(this);

    public JobFact Fact => JobFacts.FactOf(this);
}
