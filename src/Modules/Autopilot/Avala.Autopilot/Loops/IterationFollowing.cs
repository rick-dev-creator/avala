using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Loops;

internal static class IterationFollowing
{
    extension(LoopRecord loop)
    {
        public Option<IterationRecord> Following(JobId job, JobStatus status, IReadOnlyList<Cost> cost) =>
            loop.Iterations
                .LastOrDefault(iteration => iteration.Job == Option<JobId>.Some(job) && Follows(iteration.Outcome))
                .ToOption()
                .Bind(iteration => Moved(iteration, Of(status), cost));

        public LoopRecord Followed(IterationRecord iteration) => loop with
        {
            Iterations = loop.Iterations.Select(known => known.Number == iteration.Number ? iteration : known).ToImmutableList(),
        };
    }

    private static bool Follows(IterationOutcome outcome) =>
        outcome is IterationOutcome.AwaitingAnswer or IterationOutcome.AwaitingReview or IterationOutcome.NeedsHelp or IterationOutcome.CarryingOn;

    private static Option<IterationRecord> Moved(IterationRecord iteration, IterationOutcome outcome, IReadOnlyList<Cost> cost) =>
        outcome == iteration.Outcome && cost.SequenceEqual(iteration.Cost)
            ? Option<IterationRecord>.None
            : iteration with
            {
                Outcome = outcome,
                Cost = cost,
                Hold = outcome == IterationOutcome.NeedsHelp ? iteration.Hold : Option<HoldReason>.None,
                Failure = !outcome.IsFailure ? Option<FailureSignature>.None
                    : iteration.Failure.IsSome ? iteration.Failure
                    : new FailureSignature(FailureSource.Job, outcome.ToString()),
            };

    private static IterationOutcome Of(JobStatus status) => status switch
    {
        JobStatus.NeedsHelp => IterationOutcome.NeedsHelp,
        JobStatus.AwaitingReview => IterationOutcome.AwaitingReview,
        JobStatus.Approved => IterationOutcome.ApprovedByPerson,
        JobStatus.Discarded => IterationOutcome.Discarded,
        JobStatus.Failed => IterationOutcome.Failed,
        _ => IterationOutcome.CarryingOn,
    };
}
