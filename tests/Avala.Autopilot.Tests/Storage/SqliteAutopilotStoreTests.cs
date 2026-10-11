using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Loops;
using Avala.Autopilot.Sourcing;
using Avala.Autopilot.Storage;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Storage;
using Avala.Verification.Contracts;

namespace Avala.Autopilot.Tests.Storage;

public sealed class SqliteAutopilotStoreTests
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

        await using (var ledger = new SqliteAutopilotStore(paths))
        {
            await ledger.MarkAsync(greet, new TaskMark(TaskState.Taken, job, Now), Cancellation);
            await ledger.MarkAsync(greet, new TaskMark(TaskState.Approved, job, Now.AddMinutes(9)), Cancellation);
            await ledger.ProposeAsync(announce, Now.AddMinutes(5), Cancellation);
            await ledger.MarkAsync(elsewhere, new TaskMark(TaskState.Failed, Option<JobId>.None, Now), Cancellation);
        }

        await using var restarted = new SqliteAutopilotStore(paths);

        Assert.Equal(
            [new LedgerEntry(greet, TaskState.Approved, job, Now, Now.AddMinutes(9))],
            await restarted.EntriesAsync("/repositories/shop", "backlog", Cancellation));
        Assert.Equal(
            [new LedgerEntry(announce, TaskState.Proposed, Option<JobId>.None, Option<DateTimeOffset>.None, Now.AddMinutes(5))],
            await restarted.EntriesAsync("/repositories/shop", "follow-up", Cancellation));
    }

    [Fact]
    public async Task ALoopIsKeptWithItsRequestLimitsTaskUnderwayAndDigestAcrossRestartsAsync()
    {
        await using var data = new TemporaryFolder();
        var paths = new AvalaPaths(data.Path);
        var loop = Looped();

        await using (var store = new SqliteAutopilotStore(paths))
        {
            await store.KeepAsync(loop with { Status = LoopStatus.Running }, Cancellation);
            await store.KeepAsync(loop, Cancellation);
        }

        await using var restarted = new SqliteAutopilotStore(paths);
        var read = Assert.Single(await restarted.LoopsAsync(Cancellation));

        Assert.Equal(StoredJson.Write(loop.Digest), StoredJson.Write(read.Digest));
        Assert.Equal(
            (loop.Request.Connection, loop.Request.Autonomy, loop.Current, loop.Connection, StoredJson.Write(loop.Limits)),
            (read.Request.Connection, read.Request.Autonomy, read.Current, read.Connection, StoredJson.Write(read.Limits)));
    }

    [Fact]
    public async Task OnlyTheLatestEndedLoopsAreKeptAsync()
    {
        await using var data = new TemporaryFolder();
        await using var store = new SqliteAutopilotStore(new AvalaPaths(data.Path));
        var running = Looped() with { Status = LoopStatus.Running };

        await store.KeepAsync(running, Cancellation);

        foreach (var minute in Enumerable.Range(1, SqliteAutopilotStore.EndedLoopsKept + 1))
        {
            await store.KeepAsync(LoopRecord.Begin(LoopId.New(), new LoopRequest("/repositories/shop"), new LoopLimits(), Now.AddMinutes(minute)).Ended(LoopEnding.Drained), Cancellation);
        }

        var kept = await store.LoopsAsync(Cancellation);
        Assert.Equal(SqliteAutopilotStore.EndedLoopsKept + 1, kept.Count);
        Assert.Equal((running.Id, Now.AddMinutes(2)), (kept[0].Id, kept[1].Started));
    }

    private static LoopRecord Looped()
    {
        var job = JobId.New();
        var task = new SourcedTask("backlog", "greet", "/repositories/shop", "Greet the team");
        var evidence = new EvidenceSummary(1, VerificationOutcome.Passed, ["tests"], 2, 0, ["GREETING.md"]);
        var request = new LoopRequest("/repositories/shop")
        {
            Connection = new ConnectionName("work"),
            Autonomy = Autonomy.Autonomous,
            Limits = new LoopLimits { Iterations = 20, SpendPerLoop = [new Cost(5m, "USD")] },
        };

        return LoopRecord.Begin(LoopId.New(), request, new LoopLimits { Iterations = 20, PauseAtLimit = 0.8 }, Now)
            .Took(task, job)
            .Approved(new AutoApproval(LoopId.New(), job, true, evidence, [], Now) { Delivery = new ApprovalDelivery("merge", "main", "c0ffee") })
            .Settled(new IterationRecord(1, task, job, IterationOutcome.AwaitingReview, Now)
            {
                Exceptions = [ExceptionReason.RuleFileEdited],
                Hold = HoldReason.Stalled,
                Rejection = JobRejection.MergeConflict,
                Failure = new FailureSignature(FailureSource.Check, "tests Failed exit 1"),
                ChangedNothing = true,
                Cost = [new Cost(0.12m, "USD")],
            }, new ConnectionName("personal"))
            .Took(task with { Key = "docs" }, JobId.New())
            .Awaiting(underway => underway with { ContinuesAfterReset = true })
            .Paused(new LoopPause(PauseReason.UsageLimit, Now, Now.AddHours(2)) { Window = "5h" })
            .Tripped(new BreakerTrip(Breaker.SameFailure, "Check tests Failed exit 1", 2m, 2m, Now)) with { Status = LoopStatus.Paused, Ending = Option<LoopEnding>.None };
    }
}
