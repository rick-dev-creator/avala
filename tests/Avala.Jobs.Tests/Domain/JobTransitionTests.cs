using Avala.Jobs.Domain;

namespace Avala.Jobs.Tests.Domain;

public sealed class JobTransitionTests
{
    private static readonly JobState[] ReachableStates =
    [
        JobState.Draft, JobState.Preparing, JobState.Running, JobState.Checking, JobState.NeedsHelp,
        JobState.AwaitingReview, JobState.Approved, JobState.Discarded, JobState.Failed,
    ];

    private static readonly Dictionary<Operation, (JobState[] AllowedIn, JobError Rejection)> Rules = new()
    {
        [Operation.Submit] = ([JobState.Draft], JobError.CannotSubmit),
        [Operation.Start] = ([JobState.Preparing], JobError.CannotStart),
        [Operation.CompleteTurn] = ([JobState.Running], JobError.CannotCompleteTurn),
        [Operation.Pass] = ([JobState.Checking], JobError.CannotPass),
        [Operation.Retry] = ([JobState.Checking], JobError.CannotRetry),
        [Operation.RequestHelp] = ([], JobError.CannotRequestHelp),
        [Operation.Hint] = ([JobState.NeedsHelp], JobError.CannotHint),
        [Operation.SendBack] = ([JobState.AwaitingReview], JobError.CannotSendBack),
        [Operation.Approve] = ([JobState.AwaitingReview], JobError.CannotApprove),
        [Operation.Discard] =
            ([JobState.Draft, JobState.Preparing, JobState.Running, JobState.Checking, JobState.NeedsHelp, JobState.AwaitingReview], JobError.CannotDiscard),
        [Operation.Fail] = ([JobState.Preparing, JobState.Running, JobState.Checking], JobError.CannotFail),
    };

    [Fact]
    public void AppliesOnlyTheTransitionsTheLifecycleAllows()
    {
        var mismatches =
            from state in ReachableStates
            from operation in Enum.GetValues<Operation>()
            let mismatch = Mismatch(state, operation)
            where mismatch is not null
            select mismatch;

        Assert.Empty(mismatches);
    }

    private static string? Mismatch(JobState state, Operation operation)
    {
        var job = Given.JobIn(state);
        var (allowedIn, rejection) = Rules[operation];
        JobError? expected = allowedIn.Contains(state) ? null : rejection;

        var actual = Apply(job, operation);
        var expectedState = expected is null ? job.State : state;

        return actual == expected && job.State == expectedState
            ? null
            : $"{operation} in {state}: expected {expected?.ToString() ?? "success"}, got {actual?.ToString() ?? "success"} ending in {job.State}";
    }

    private static JobError? Apply(Job job, Operation operation) => operation switch
    {
        Operation.Submit => Outcomes.ErrorOf(job.Submit()),
        Operation.Start => Outcomes.ErrorOf(job.Start()),
        Operation.CompleteTurn => Outcomes.ErrorOf(job.CompleteTurn()),
        Operation.Pass => Outcomes.ErrorOf(job.Pass()),
        Operation.Retry => Outcomes.ErrorOf(job.Retry(Given.Feedback)),
        Operation.RequestHelp => Outcomes.ErrorOf(job.RequestHelp()),
        Operation.Hint => Outcomes.ErrorOf(job.Hint(Given.Feedback)),
        Operation.SendBack => Outcomes.ErrorOf(job.SendBack(Given.Feedback)),
        Operation.Approve => Outcomes.ErrorOf(job.Approve()),
        Operation.Discard => Outcomes.ErrorOf(job.Discard()),
        Operation.Fail => Outcomes.ErrorOf(job.Fail(FailureReason.AgentFailed)),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };
}
