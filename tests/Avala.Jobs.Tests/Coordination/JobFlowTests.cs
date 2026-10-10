using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Avala.Testing;
using HoldAnnouncement = Avala.Jobs.Contracts.JobHeld;
using JobAnnouncement = Avala.Jobs.Contracts.JobSubmitted;

namespace Avala.Jobs.Tests.Coordination;

public sealed class JobFlowTests
{
    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task SubmittingStoresThePreparingJobAndAnnouncesItAsync()
    {
        var flow = JobFlow.With();

        var job = await flow.SubmittedAsync();

        Assert.Equal(JobState.Preparing, job.State);
        Assert.Contains(new JobProgressed(job.Id, JobStatus.Preparing), flow.Bus.Published);
        Assert.Contains(new JobAnnouncement(job.Id), flow.Bus.Published);
    }

    [Fact]
    public async Task PreparingOpensTheWorkspaceStartsTheAgentThereAndSendsTheInstructionAsync()
    {
        var flow = JobFlow.With();

        var job = await flow.RunningAsync();

        Assert.Equal(JobState.Running, job.State);
        Assert.Equal(["/repos/shop"], flow.Workspaces.Requests);
        var (session, folder) = Assert.Single(flow.Agents.Sessions);
        Assert.StartsWith("/worktrees/", folder, StringComparison.Ordinal);
        Assert.Equal([(session, "Add GitHub login")], flow.Agents.Sent);
        Assert.Equal(Option<SessionId>.Some(session), job.Session);
    }

    [Fact]
    public async Task AWorkspaceThatCannotBePreparedFailsTheJobAsync()
    {
        var flow = new JobFlow(new FakeWorkspaces { PreparationFails = true }, new FakeAgents());

        var job = await flow.RunningAsync();

        Assert.Equal(JobState.Failed, job.State);
        Assert.Empty(flow.Agents.Sessions);
    }

    [Fact]
    public async Task AnAgentThatCannotOpenFailsTheJobAsync()
    {
        var flow = new JobFlow(new FakeWorkspaces(), new FakeAgents { OpeningFails = true });

        var job = await flow.RunningAsync();

        Assert.Equal(JobState.Failed, job.State);
    }

    [Fact]
    public async Task WithoutGatesAFinishedTurnIsCheckpointedAndGoesToReviewAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();

        await flow.FinishTurnAsync(job);

        Assert.Equal(JobState.AwaitingReview, job.State);
        Assert.Equal(["Attempt 1"], flow.Workspaces.Checkpoints);
        Assert.Contains(new JobProgressed(job.Id, JobStatus.AwaitingReview), flow.Bus.Published);
    }

    [Fact]
    public async Task GatesReceiveTheAttemptToJudgeAsync()
    {
        var gate = new ScriptedGate();
        var flow = JobFlow.With(gate);
        var job = await flow.RunningAsync();

        await flow.FinishTurnAsync(job);

        var attempt = Assert.Single(gate.Evaluated);
        Assert.Equal((job.Id, 1, "Add GitHub login"), (attempt.Job, attempt.Attempt, attempt.Instruction));
        Assert.StartsWith("/worktrees/", attempt.WorkingDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARetryVerdictSendsItsFeedbackToTheSameSessionAsync()
    {
        var flow = JobFlow.With(new ScriptedGate(GateVerdict.Retry("Two tests fail")));
        var job = await flow.RunningAsync();

        await flow.FinishTurnAsync(job);

        Assert.Equal(JobState.Running, job.State);
        Assert.Equal(2, job.Attempts.Count);
        Assert.Equal(["Add GitHub login", "Two tests fail"], flow.Agents.Sent.Select(message => message.Message));
        Assert.Single(flow.Agents.Sent.Select(message => message.Session).Distinct());
    }

    [Fact]
    public async Task AHoldVerdictHoldsTheJobForItsReasonInsteadOfSendingItToReviewOrAskingLaterGatesAsync()
    {
        var later = new ScriptedGate();
        var flow = JobFlow.With(new ScriptedGate(GateVerdict.HoldFor(HoldReason.BudgetExceeded)), later);
        var job = await flow.RunningAsync();

        await flow.FinishTurnAsync(job);

        Assert.Equal(JobState.NeedsHelp, job.State);
        Assert.Equal(HoldReason.BudgetExceeded, Assert.Single(flow.Bus.Published.OfType<HoldAnnouncement>()).Hold.Reason);
        Assert.DoesNotContain(new JobProgressed(job.Id, JobStatus.AwaitingReview), flow.Bus.Published);
        Assert.Empty(later.Evaluated);
    }

    [Fact]
    public async Task WhenRetriesRunOutTheJobAsksForHelpAsync()
    {
        var flow = JobFlow.With(new ScriptedGate(GateVerdict.Retry("Two tests fail")));
        var job = await flow.RunningAsync(attemptsPerRound: 1);

        await flow.FinishTurnAsync(job);

        Assert.Equal(JobState.NeedsHelp, job.State);
        Assert.Single(flow.Agents.Sent);
    }

    [Fact]
    public async Task AFailedTurnFailsTheJobAndStopsItsSessionBeforeAnnouncingItAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();
        var session = Outcomes.Present(job.Session);

        await flow.FinishTurnAsync(job, TurnOutcome.Failed);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal([session], flow.Agents.Stopped);
    }

    [Fact]
    public async Task AJobWaitsForItsAdmissionBeforeItsWorkspaceIsPreparedAsync()
    {
        var flow = JobFlow.With();
        var admission = new GatedAdmission();
        flow.Admissions.Add(admission);
        var job = await flow.SubmittedAsync();

        var preparing = flow.Prepare.HandleAsync(new JobAnnouncement(job.Id), Cancellation).AsTask();
        var asked = await admission.Asked.Task.WaitAsync(Cancellation);
        Assert.Empty(flow.Workspaces.Requests);

        admission.Open();
        await preparing;
        await flow.SettledAsync(job);

        Assert.Equal(job.Id, asked);
        Assert.Equal(JobState.Running, job.State);
    }

    private sealed class GatedAdmission : IJobAdmission
    {
        private readonly TaskCompletionSource admitted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<JobId> Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open() => admitted.SetResult();

        public async ValueTask AdmitAsync(JobId job, CancellationToken cancellationToken)
        {
            Asked.SetResult(job);
            await admitted.Task.WaitAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task ATurnFinishedTwiceIsAppliedOnceAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();

        await flow.FinishTurnAsync(job);
        await flow.FinishTurnAsync(job);

        Assert.Equal(JobState.AwaitingReview, job.State);
        Assert.Single(flow.Workspaces.Checkpoints);
    }

    [Fact]
    public async Task ATurnBeingCheckedForOneJobDoesNotDelayTheChecksOfAnotherJobAsync()
    {
        var gate = new BlockingGate();
        var flow = JobFlow.With(gate);
        var slow = await flow.RunningAsync();
        var fast = await flow.RunningAsync();

        await flow.HandTurnAsync(slow);
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        await flow.FinishTurnAsync(fast);

        Assert.Equal((JobState.Checking, JobState.AwaitingReview), (slow.State, fast.State));
        gate.Release();
        await flow.SettledAsync(slow);
        Assert.Equal(JobState.AwaitingReview, slow.State);
    }

    [Fact]
    public async Task ATurnFromAnUnknownSessionIsIgnoredAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();

        await flow.Check.HandleAsync(new TurnFinished(SessionId.New(), TurnId.New(), TurnOutcome.Finished), Cancellation);

        Assert.Equal(JobState.Running, job.State);
        Assert.Empty(flow.Workspaces.Checkpoints);
    }

    [Fact]
    public async Task RecoveryLaunchesPreparingJobsAndResumesRunningOnesInANewSessionAsync()
    {
        var flow = JobFlow.With();
        var preparing = await flow.SubmittedAsync();
        var running = await flow.RunningAsync();
        var lostSession = running.Session;

        await flow.Recovery.RunAsync(Cancellation);

        Assert.Equal(JobState.Running, preparing.State);
        Assert.Equal(JobState.Running, running.State);
        Assert.NotEqual(lostSession, running.Session);
        Assert.Equal([AttemptOrigin.Initial, AttemptOrigin.Recovery], running.Attempts.Select(attempt => attempt.Origin));
        Assert.Equal(3, flow.Agents.Sessions.Count);
    }

    [Fact]
    public async Task RecoveryLeavesTheJobsThisRunAlreadySubmittedOrStartedToTheirOwnFlowAsync()
    {
        var flow = JobFlow.With();
        var waiting = await flow.SubmittedAsync();
        var running = await flow.RunningAsync();
        var session = running.Session;

        await flow.RecoveryInThisRun.RunAsync(Cancellation);

        Assert.Equal((JobState.Preparing, JobState.Running), (waiting.State, running.State));
        Assert.Equal(session, running.Session);
        Assert.Single(flow.Agents.Sessions);
    }

    [Fact]
    public async Task EverySessionAJobStartsIsAnnouncedWithTheAutonomyTheJobAskedForAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync(autonomy: Autonomy.Supervised);
        var first = Assert.Single(flow.Agents.Sessions).Key;

        await flow.Recovery.RunAsync(Cancellation);

        var second = Assert.Single(flow.Agents.Sessions.Keys, session => session != first);
        Assert.Equal(
            [new JobSessionStarted(job.Id, first) { Autonomy = Autonomy.Supervised }, new JobSessionStarted(job.Id, second) { Autonomy = Autonomy.Supervised }],
            flow.Bus.Published.OfType<JobSessionStarted>());
    }
}
