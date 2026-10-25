using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Handoffs.Watching;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Handoffs.Tests.Watching;

public sealed class RelayAdversaryTests
{
    private static readonly LimitRules Auto = LimitRules.Default with { Resume = ResumeAtReset.Auto };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnotherJobHeldAtItsLimitIsLookedAfterWhileOneJobIsStillCompactingAsync()
    {
        await using var watched = await CompactingAsync();
        var other = Another(watched);

        await watched.Watch.HandleAsync(new JobHeld(new JobHold(other, SessionId.New(), HoldReason.LimitNearlyReached, SessionHalt.Interrupted)), Cancellation);

        _ = await watched.Bus.WaitForAsync<JobWaitsForReset>(found => found.Wait.Job == other, Cancellation);
        Assert.DoesNotContain(watched.Jobs.Calls.Entries, call => call.Job == watched.Job);
    }

    [Fact]
    public async Task AJobThatAPersonContinuedWhileItWasCompactingResumesAtItsNextResetAsync()
    {
        await using var watched = await CompactingAsync();
        watched.Status(JobStatus.Running);
        await watched.ProgressedAsync(JobStatus.Running);
        watched.Status(JobStatus.NeedsHelp);
        await watched.HeldAsync(HoldReason.LimitNearlyReached);
        _ = await watched.Bus.WaitForAsync<JobWaitsForReset>(found => found.Wait.Since > Watched.Start, Cancellation);

        watched.Clock.Advance(Compaction.Patience);
        watched.Agents.Answer = (session, turn) => watched.Compaction.HandleAsync(new TurnFinished(session, turn, TurnOutcome.Finished), Cancellation).AsTask();
        watched.Clock.Advance(TimeSpan.FromHours(5));
        var resumed = await watched.Bus.WaitForAsync<JobResumedAtReset>(found => found.Job == watched.Job, Cancellation);

        Assert.Equal(watched.Clock.GetUtcNow(), resumed.At);
    }

    private static async Task<Watched> CompactingAsync()
    {
        var watched = new Watched();
        watched.Rules.Rules = Auto;
        watched.Connections.Compacts = true;
        watched.Agents.Open.Add(watched.Session);
        watched.Agents.Answer = (_, _) => Task.CompletedTask;
        watched.Reading(Watched.Work, 0.95, Watched.Start.AddHours(1));
        await watched.HeldAsync(HoldReason.LimitNearlyReached);
        _ = await watched.WaitingAsync();
        watched.Clock.Advance(TimeSpan.FromHours(1));
        _ = await watched.Clock.Timers.WaitForAsync(due => due == Compaction.Patience);
        watched.Reading(Watched.Work, 0.95, Watched.Start.AddHours(6));

        return watched;
    }

    private static JobId Another(Watched watched)
    {
        var other = JobId.New();
        watched.Catalog.Histories[other] = new JobHistory(
            new JobSummary(other, "/repos/shop", Watched.Instruction, Watched.Start, JobStatus.NeedsHelp, Watched.Work, Option<Autonomy>.None, WorkspaceId.New()),
            [new SessionRecord(SessionId.New(), [1])],
            []);

        return other;
    }
}
