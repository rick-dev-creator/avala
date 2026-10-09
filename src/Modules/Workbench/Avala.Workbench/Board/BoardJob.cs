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

    public int Revision { get; init; }

    public int PendingDecisions => Transcript.Awaiting.Count();

    public JobGroup Group => JobFacts.GroupOf(this);

    public JobFact Fact => JobFacts.FactOf(this);
}
