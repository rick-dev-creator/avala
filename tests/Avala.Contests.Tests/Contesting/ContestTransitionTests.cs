using Avala.Contests.Contesting;
using Avala.Contests.Contracts;
using Avala.Testing;

namespace Avala.Contests.Tests.Contesting;

public sealed class ContestTransitionTests
{
    private static readonly Dictionary<Operation, (ContestStatus[] AllowedIn, ContestError Rejection)> Rules = new()
    {
        [Operation.Settle] = ([ContestStatus.Running, ContestStatus.Judging, ContestStatus.Decided, ContestStatus.Undecided], ContestError.CannotSettle),
        [Operation.Reopen] = ([ContestStatus.Judging, ContestStatus.Decided, ContestStatus.Undecided], ContestError.CannotReopen),
        [Operation.Decide] = ([ContestStatus.Judging], ContestError.CannotDecide),
        [Operation.Close] = ([ContestStatus.Running, ContestStatus.Judging, ContestStatus.Decided, ContestStatus.Undecided], ContestError.CannotClose),
    };

    private enum Operation
    {
        Settle,
        Reopen,
        Decide,
        Close,
    }

    [Fact]
    public void AppliesOnlyTheTransitionsTheLifecycleAllows()
    {
        var mismatches =
            from status in Enum.GetValues<ContestStatus>()
            from operation in Enum.GetValues<Operation>()
            let mismatch = Mismatch(status, operation)
            where mismatch is not null
            select mismatch;

        Assert.Empty(mismatches);
    }

    private static string? Mismatch(ContestStatus status, Operation operation)
    {
        var contest = Given.ContestIn(status);
        var (allowedIn, rejection) = Rules[operation];
        ContestError? expected = allowedIn.Contains(status) ? null : rejection;

        var actual = Apply(contest, operation);

        return actual == expected && (expected is null || contest.Status == status)
            ? null
            : $"{operation} in {status}: expected {expected?.ToString() ?? "success"}, got {actual?.ToString() ?? "success"} ending in {contest.Status}";
    }

    private static ContestError? Apply(Contest contest, Operation operation) => operation switch
    {
        Operation.Settle => Outcomes.ErrorOf(contest.Settle(contest.Candidates[0].Job, CandidateOutcome.NeedsHelp, Given.At)),
        Operation.Reopen => Outcomes.ErrorOf(contest.Reopen(contest.Candidates[0].Job)),
        Operation.Decide => Outcomes.ErrorOf(contest.Decide(contest.Round, Given.Verdict(contest.Candidates[0].Job))),
        Operation.Close => Outcomes.ErrorOf(contest.Close(contest.Candidates[0].Job, Given.At)),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };
}
