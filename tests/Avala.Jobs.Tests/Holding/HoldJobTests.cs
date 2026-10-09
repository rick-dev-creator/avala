using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Tests.Coordination;
using Avala.Sdk;
using Avala.Testing;
using HoldAnnouncement = Avala.Jobs.Contracts.JobHeld;

namespace Avala.Jobs.Tests.Holding;

public sealed class HoldJobTests
{
    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task HoldingARunningJobAsksForHelpInterruptsItsTurnAndAnnouncesWhyAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();
        var session = Outcomes.Present(job.Session);

        var hold = Outcomes.Succeeds(await flow.Jobs.HoldAsync(job.Id, HoldReason.Stalled, Cancellation));

        Assert.Equal(new JobHold(job.Id, session, HoldReason.Stalled, SessionHalt.Interrupted), hold);
        Assert.Equal(JobState.NeedsHelp, job.State);
        Assert.Equal([session], flow.Agents.Interrupted);
        Assert.Equal(
            [new JobProgressed(job.Id, JobStatus.NeedsHelp), new HoldAnnouncement(hold)],
            flow.Bus.Published.SkipWhile(published => published is not JobProgressed { Status: JobStatus.NeedsHelp }));
    }

    [Theory]
    [InlineData(AgentError.Unsupported, SessionHalt.Stopped)]
    [InlineData(AgentError.NoTurnInProgress, SessionHalt.Idle)]
    public async Task ASessionThatCannotBeInterruptedIsStoppedAndAnIdleOneIsLeftOpenAsync(AgentError rejection, SessionHalt expected)
    {
        var flow = new JobFlow(new FakeWorkspaces(), new FakeAgents { InterruptRejection = rejection });
        var job = await flow.RunningAsync();

        var hold = Outcomes.Succeeds(await flow.Jobs.HoldAsync(job.Id, HoldReason.BudgetExceeded, Cancellation));

        Assert.Equal(expected, hold.Halt);
        Assert.Equal(expected == SessionHalt.Stopped, flow.Agents.Stopped.Contains(hold.Session));
    }

    [Theory]
    [InlineData(HoldReason.SessionLost)]
    [InlineData(HoldReason.Stopped)]
    public async Task ALostSessionOrAJobAPersonStoppedIsStoppedRatherThanInterruptedAsync(HoldReason reason)
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();

        var hold = Outcomes.Succeeds(await flow.Jobs.HoldAsync(job.Id, reason, Cancellation));

        Assert.Equal(SessionHalt.Stopped, hold.Halt);
        Assert.Empty(flow.Agents.Interrupted);
        Assert.Equal([hold.Session], flow.Agents.Stopped);
    }

    [Fact]
    public async Task OnlyARunningJobCanBeHeldAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.SubmittedAsync();
        var published = flow.Bus.Published.Count;

        Assert.Equal(JobRejection.NotRunning, Outcomes.FailsWith(await flow.Jobs.HoldAsync(job.Id, HoldReason.Stalled, Cancellation)));
        Assert.Equal(JobRejection.UnknownJob, Outcomes.FailsWith(await flow.Jobs.HoldAsync(JobId.New(), HoldReason.Stalled, Cancellation)));
        Assert.Equal(JobState.Preparing, job.State);
        Assert.Equal(published, flow.Bus.Published.Count);
    }

    [Fact]
    public async Task TheEndOfTheTurnAHoldInterruptedLeavesTheJobHeldAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();
        Outcomes.Succeeds(await flow.Jobs.HoldAsync(job.Id, HoldReason.Stalled, Cancellation));

        await flow.FinishTurnAsync(job, TurnOutcome.Interrupted);

        Assert.Equal(JobState.NeedsHelp, job.State);
        Assert.Empty(flow.Workspaces.Checkpoints);
    }

    [Fact]
    public async Task ASessionThatEndsOnItsOwnHoldsItsJobAsSessionLostBeforeItsFailedTurnIsCheckedAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();
        var session = Outcomes.Present(job.Session);

        await flow.Check.HandleAsync(new SessionEnded(session, SessionEnding.Crashed), Cancellation);
        await flow.FinishTurnAsync(job, TurnOutcome.Failed);

        var held = Assert.Single(flow.Bus.Published.OfType<HoldAnnouncement>()).Hold;
        Assert.Equal((JobState.NeedsHelp, job.Id, session, HoldReason.SessionLost), (job.State, held.Job, held.Session, held.Reason));
    }

    [Fact]
    public async Task TheEndOfASessionTheJobNoLongerUsesIsIgnoredAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();
        var replaced = Outcomes.Present(job.Session);
        await flow.Recovery.RunAsync(Cancellation);

        await flow.EndSessionAsync(job, replaced);

        Assert.Equal(JobState.Running, job.State);
        Assert.Empty(flow.Bus.Published.OfType<HoldAnnouncement>());
    }

    [Fact]
    public async Task AHoldRequestedWhileTheJobsTurnIsCheckedWaitsForTheCheckAsync()
    {
        var gate = new BlockingGate();
        var flow = JobFlow.With(gate);
        var job = await flow.RunningAsync();
        await flow.HandTurnAsync(job);
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        var hold = flow.Jobs.HoldAsync(job.Id, HoldReason.Stalled, Cancellation).AsTask();

        Assert.False(hold.IsCompleted);
        gate.Release();
        Assert.Equal(JobRejection.NotRunning, Outcomes.FailsWith(await hold.WaitAsync(TimeSpan.FromSeconds(10), Cancellation)));
        Assert.Equal(JobState.AwaitingReview, job.State);
    }

    [Fact]
    public async Task AJobHeldBeforeItsInstructionIsSentIsNeverToldAsync()
    {
        var flow = JobFlow.With();
        flow.Store.Saving = job =>
        {
            if (job.State == JobState.Running)
            {
                Outcomes.Succeeds(job.Hold(HoldReason.InvalidBudget));
            }
        };

        var job = await flow.RunningAsync();

        Assert.Equal(JobState.NeedsHelp, job.State);
        Assert.Empty(flow.Agents.Sent);
    }
}
