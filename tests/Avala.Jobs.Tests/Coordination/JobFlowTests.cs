using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.Logging;
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
        var flow = new JobFlow(new FakeWorkspaces { PreparationFailure = WorkspaceFailure.NotAGitRepository }, new FakeAgents());

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
    public async Task ARetryVerdictSendsItsFeedbackFollowedByEveryBriefingAsync()
    {
        var flow = JobFlow.With(new ScriptedGate(GateVerdict.Retry("Two tests fail")));
        flow.Briefings.Add(new NoteBriefing("Bring your task list up to date."));
        var job = await flow.RunningAsync();

        await flow.FinishTurnAsync(job);

        Assert.Equal("Two tests fail\n\nBring your task list up to date.", flow.Agents.Sent[^1].Message);
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
    public async Task ADelegatedChildsFailedTurnHoldsItAsTurnFailedSoItsDelegationCanRetryItInsteadOfFailingItAsync()
    {
        var flow = JobFlow.With();
        var parent = await flow.RunningAsync();
        var child = await flow.RunningAsync(JobFlow.Request() with { Parent = parent.Id });
        flow.Retries.Add(new Retrying(child.Id));

        await flow.FinishTurnAsync(child, TurnOutcome.Failed);

        Assert.Equal((JobState.NeedsHelp, Option<HoldReason>.Some(HoldReason.TurnFailed)), (child.State, child.Held));
    }

    [Fact]
    public async Task AChildWhoseDelegationNoLongerRetriesItFailsItsFailedTurnInsteadOfWaitingForARetryAsync()
    {
        var flow = JobFlow.With();
        var parent = await flow.RunningAsync();
        var child = await flow.RunningAsync(JobFlow.Request() with { Parent = parent.Id });
        flow.Retries.Add(new Retrying(JobId.New()));

        await flow.FinishTurnAsync(child, TurnOutcome.Failed);

        Assert.Equal(JobState.Failed, child.State);
    }

    private sealed class Retrying(JobId retried) : IChildRetries
    {
        public ValueTask<bool> RetriesAsync(JobId child, CancellationToken cancellationToken) => ValueTask.FromResult(child == retried);
    }

    [Fact]
    public async Task ACompanionsFailedTurnFailsItBecauseNoDelegationRetriesItAsync()
    {
        var flow = JobFlow.With();
        var parent = await flow.RunningAsync();
        var companion = await flow.RunningAsync(JobFlow.Request() with { Parent = parent.Id, Companion = true });

        await flow.FinishTurnAsync(companion, TurnOutcome.Failed);

        Assert.Equal(JobState.Failed, companion.State);
    }

    [Fact]
    public async Task ATurnThatRanOutOfItsUsageWindowHoldsTheJobNearItsLimitWithThatWindowInsteadOfFailingItAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();
        var session = Outcomes.Present(job.Session);
        var spent = new UsageLimit("5h", 1, DateTimeOffset.UnixEpoch.AddHours(5));

        await flow.Check.HandleAsync(new TurnFinished(session, TurnId.New(), TurnOutcome.LimitReached) { Limit = spent }, Cancellation);
        await flow.SettledAsync(job);

        var held = Assert.Single(flow.Bus.Published.OfType<HoldAnnouncement>()).Hold;
        Assert.Equal((JobState.NeedsHelp, HoldReason.LimitNearlyReached, Option<UsageLimit>.Some(spent)), (job.State, held.Reason, held.Limit));
        Assert.Equal(AttemptOutcome.Interrupted, job.Attempts[^1].Outcome);
        Assert.Empty(flow.Agents.Stopped);
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

    [Fact]
    public async Task AReportOwedToAJobWhoseRetryCannotReachItsAgentIsDeliveredWhenTheJobContinuesAsync()
    {
        var flow = JobFlow.With(new ScriptedGate(GateVerdict.Retry("Two tests fail")));
        var owed = new OwedOnce();
        flow.Briefings.Add(owed);
        var job = await flow.RunningAsync();
        var session = Outcomes.Present(job.Session);
        owed.Owe("The child finished: refunds are idempotent.");
        _ = await flow.Agents.StopAsync(session, Cancellation);

        await flow.FinishTurnAsync(job);
        await flow.EndSessionAsync(job, session);
        _ = Outcomes.Succeeds(await flow.Jobs.ContinueAsync(job.Id, new JobMessage("Go on"), Cancellation));
        await flow.SettledAsync(job);

        Assert.Contains(flow.Agents.Sent, sent => sent.Message.Contains("The child finished", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AReportOwedToAJobWhoseSessionClosesWhileItIsBriefedIsDeliveredWhenTheJobContinuesAsync()
    {
        var flow = JobFlow.With(new ScriptedGate(GateVerdict.Retry("Two tests fail")));
        var owed = new OwedOnce();
        flow.Briefings.Add(owed);
        var job = await flow.RunningAsync();
        var session = Outcomes.Present(job.Session);
        flow.Briefings.Add(new SessionClosing(flow.Agents, session));
        owed.Owe("The child finished: refunds are idempotent.");

        await flow.FinishTurnAsync(job);
        await flow.EndSessionAsync(job, session);
        _ = Outcomes.Succeeds(await flow.Jobs.ContinueAsync(job.Id, new JobMessage("Go on"), Cancellation));
        await flow.SettledAsync(job);

        Assert.Contains(flow.Agents.Sent, sent => sent.Message.Contains("The child finished", StringComparison.Ordinal));
    }

    private sealed class SessionClosing(FakeAgents agents, SessionId session) : IJobBriefing
    {
        public async ValueTask<Option<JobBrief>> BriefAsync(JobId job, CancellationToken cancellationToken)
        {
            _ = await agents.StopAsync(session, cancellationToken);

            return Option<JobBrief>.None;
        }
    }

    [Fact]
    public async Task ABriefingWhoseMessageThrowsOnTheWayToTheAgentIsReleasedAsync()
    {
        var flow = JobFlow.With(new ScriptedGate(GateVerdict.Retry("Two tests fail")));
        var briefing = new SettledBriefing();
        flow.Briefings.Add(briefing);
        var job = await flow.RunningAsync();
        briefing.Armed = true;
        flow.Agents.SayFault = new OperationCanceledException("The send was cancelled");

        _ = await Record.ExceptionAsync(async () => await flow.FinishTurnAsync(job));

        Assert.Equal("undelivered", briefing.Settlement);
    }

    private sealed class SettledBriefing : IJobBriefing
    {
        public bool Armed { get; set; }

        public string Settlement { get; private set; } = "unsettled";

        public ValueTask<Option<JobBrief>> BriefAsync(JobId job, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Armed
                ? Option<JobBrief>.Some(new JobBrief("The child finished.")
                {
                    Delivered = _ => SettledAsync("delivered"),
                    Undelivered = _ => SettledAsync("undelivered"),
                })
                : Option<JobBrief>.None);

        private Task SettledAsync(string settlement)
        {
            Settlement = settlement;

            return Task.CompletedTask;
        }
    }

    private sealed class OwedOnce : IJobBriefing
    {
        private string? report;

        public void Owe(string owed) => report = owed;

        public ValueTask<Option<JobBrief>> BriefAsync(JobId job, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Volatile.Read(ref report) is { } briefed
                ? Option<JobBrief>.Some(new JobBrief(briefed) { Delivered = _ => Task.FromResult(Interlocked.CompareExchange(ref report, null, briefed)) })
                : Option<JobBrief>.None);
    }

    private sealed class NoteBriefing(string note) : IJobBriefing
    {
        public ValueTask<Option<JobBrief>> BriefAsync(JobId job, CancellationToken cancellationToken) => ValueTask.FromResult(Option<JobBrief>.Some(new JobBrief(note)));
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

    [Fact]
    public async Task EveryHoldIsLoggedWithItsReasonAsync()
    {
        var flow = JobFlow.With();

        var job = await flow.HeldAsync(HoldReason.Stalled);

        var entry = Assert.Single(flow.Journal.Entries);
        Assert.Equal((LogLevel.Warning, $"Job {job.Id.Value} is held as Stalled, its session Interrupted"), (entry.Level, entry.Message));
    }

    [Fact]
    public async Task AnInterruptNeverConfirmedIsLoggedWithTheJobItsConnectionAndItsSessionAsync()
    {
        var flow = new JobFlow(new FakeWorkspaces(), new FakeAgents { InterruptRejection = AgentError.InterruptUnconfirmed });
        var job = await flow.RunningAsync();
        var session = Outcomes.Present(job.Session);

        Outcomes.Succeeds(await flow.Jobs.HoldAsync(job.Id, HoldReason.Stalled, Cancellation));

        Assert.Equal(
            (LogLevel.Warning, $"Job {job.Id.Value} on default never confirmed the interrupt of its session {session.Value}, so the session is stopped"),
            (flow.Journal.Entries[0].Level, flow.Journal.Entries[0].Message));
    }

    [Fact]
    public async Task EveryRefusedMessageIsLoggedWithWhyAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();

        Assert.Equal(JobRejection.NotHeld, Outcomes.FailsWith(await flow.Jobs.ContinueAsync(job.Id, "Use the staging database", Cancellation)));

        var entry = Assert.Single(flow.Journal.Entries);
        Assert.Equal((LogLevel.Warning, $"A message to job {job.Id.Value} was refused: NotHeld"), (entry.Level, entry.Message));
    }
}
