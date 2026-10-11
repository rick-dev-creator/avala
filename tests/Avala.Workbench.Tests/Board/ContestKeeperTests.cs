using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;

namespace Avala.Workbench.Tests.Board;

public sealed class ContestKeeperTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheBoardKeepsEachCandidatesContestAsItNowStandsAndCountsItsChangesInTheJobsRevisionAsync()
    {
        var board = new JobBoard();
        var keeper = new ContestKeeper(board);
        var (first, second) = (JobId.New(), JobId.New());
        var running = FakeContests.Contest(ContestStatus.Running, first, second);
        await keeper.HandleAsync(new ContestsRestored([running]), Cancellation);
        var before = board.Revision(first);

        await keeper.HandleAsync(new ContestDecided(running with { Status = ContestStatus.Decided }), Cancellation);

        Assert.Equal(ContestStatus.Decided, Outcomes.Present(board.ContestOf(second)).Contest.Status);
        Assert.True(board.Revision(first) > before);
        Assert.Equal(Option<BoardContest>.None, board.ContestOf(JobId.New()));
    }
}
