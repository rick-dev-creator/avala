using Avala.Autopilot.Contracts;
using Avala.Autopilot.Sourcing;
using Avala.Autopilot.Storage;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Autopilot.Tests.Storage;

public sealed class SqliteTaskLedgerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 22, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MarksAndProposalsAreKeptInAvalasOwnStoreAcrossRestartsByRepositoryAndSourceAsync()
    {
        await using var data = new TemporaryFolder();
        var paths = new AvalaPaths(data.Path);
        var greet = new SourcedTask("backlog", "greet", "/repositories/shop", "Greet the team");
        var announce = new SourcedTask("follow-up", "a1", "/repositories/shop", "Announce the greeting");
        var elsewhere = new SourcedTask("backlog", "greet", "/repositories/other", "Greet the team");
        var job = JobId.New();

        await using (var ledger = new SqliteTaskLedger(paths))
        {
            await ledger.MarkAsync(greet, new TaskMark(TaskState.Taken, job, Now), Cancellation);
            await ledger.MarkAsync(greet, new TaskMark(TaskState.Approved, job, Now.AddMinutes(9)), Cancellation);
            await ledger.ProposeAsync(announce, Now.AddMinutes(5), Cancellation);
            await ledger.MarkAsync(elsewhere, new TaskMark(TaskState.Failed, Option<JobId>.None, Now), Cancellation);
        }

        await using var restarted = new SqliteTaskLedger(paths);

        Assert.Equal(
            [new LedgerEntry(greet, TaskState.Approved, job, Now, Now.AddMinutes(9))],
            await restarted.EntriesAsync("/repositories/shop", "backlog", Cancellation));
        Assert.Equal(
            [new LedgerEntry(announce, TaskState.Proposed, Option<JobId>.None, Option<DateTimeOffset>.None, Now.AddMinutes(5))],
            await restarted.EntriesAsync("/repositories/shop", "follow-up", Cancellation));
    }
}
