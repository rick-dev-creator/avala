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

    [Fact]
    public async Task ALostSessionIsStoppedRatherThanInterruptedAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();

        var hold = Outcomes.Succeeds(await flow.Jobs.HoldAsync(job.Id, HoldReason.SessionLost, Cancellation));

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
