using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Workbench.Board;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Board;

public sealed class JobBoardTests
{
    [Fact]
    public async Task AWatcherIsToldOfEveryChangeAndReadsTheLatestBoard()
    {
        var board = new JobBoard();
        var summary = new FakeCatalog().Add("Fix the failing test").Summary;
        using var stop = new CancellationTokenSource();
        await using var changes = board.ChangesAsync(stop.Token).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await changes.MoveNextAsync());
        Assert.Empty(board.Jobs);
        board.Publish(ImmutableDictionary<JobId, BoardJob>.Empty.Add(summary.Job, new BoardJob(summary, Transcript.Empty)));

        Assert.True(await changes.MoveNextAsync());
        Assert.Equal([summary.Job], board.Jobs.Keys);
        await stop.CancelAsync();
        Assert.False(await changes.MoveNextAsync());
    }
}
