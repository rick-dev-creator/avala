using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Avala.Testing;
using HoldReason = Avala.Jobs.Contracts.HoldReason;

namespace Avala.Jobs.Tests.Jobs;

public sealed class JobLifecycleTests
{
    [Fact]
    public void ANewJobIsADraftWithoutAttempts()
    {
        var job = Given.Job();

        Assert.Equal(JobState.Draft, job.State);
        Assert.Empty(job.Attempts);
    }

    [Fact]
    public void SubmittingAnnouncesTheJob()
    {
        var job = Given.Job();

        Assert.Equal(new JobSubmitted(job.Id), Outcomes.Succeeds(job.Submit()));
        Assert.Equal(JobState.Preparing, job.State);
    }

    [Fact]
    public void StartingOpensTheFirstAttemptInItsWorkspaceAndSession()
    {
        var job = Given.JobIn(JobState.Preparing);

        var started = Outcomes.Succeeds(job.Start(Given.Workspace, Given.Session));

        Assert.Equal(new AttemptStarted(job.Id, AttemptNumber.First, AttemptOrigin.Initial, Option<Feedback>.None), started);
        Assert.Equal(AttemptOutcome.Running, Assert.Single(job.Attempts).Outcome);
        Assert.Equal(Given.Workspace, job.Workspace);
        Assert.Equal(Given.Session, job.Session);
    }

    [Fact]
    public void RecoveringInterruptsTheAttemptAndResumesInANewSession()
    {
        var job = Given.JobIn(JobState.Running);
        var session = SessionId.New();

        var resumed = Outcomes.Succeeds(job.Recover(session, resumed: false));

        Assert.Equal(new AttemptStarted(job.Id, new AttemptNumber(2), AttemptOrigin.Recovery, Option<Feedback>.None), resumed);
        Assert.Equal([AttemptOutcome.Interrupted, AttemptOutcome.Running], job.Attempts.Select(attempt => attempt.Outcome));
        Assert.Equal(session, job.Session);
        Assert.Equal(JobState.Running, job.State);
    }

    [Fact]
    public void CompletingTheTurnAwaitsTheCheck()
    {
        var job = Given.JobIn(JobState.Running);

        Assert.Equal(new AttemptCompleted(job.Id, AttemptNumber.First), Outcomes.Succeeds(job.CompleteTurn()));
        Assert.Equal(AttemptOutcome.AwaitingCheck, job.Attempts[^1].Outcome);
    }

    [Fact]
    public void PassingSendsTheJobToReview()
    {
        var job = Given.JobIn(JobState.Checking);

        Assert.Equal(new AttemptPassed(job.Id, AttemptNumber.First), Outcomes.Succeeds(job.Pass()));
        Assert.Equal(JobState.AwaitingReview, job.State);
        Assert.Equal(AttemptOutcome.Passed, job.Attempts[^1].Outcome);
    }

    [Fact]
    public void ApprovingFinishesTheJob()
    {
        var job = Given.JobIn(JobState.AwaitingReview);

        Assert.Equal(new JobApproved(job.Id), Outcomes.Succeeds(job.Approve()));
        Assert.Equal(JobState.Approved, job.State);
    }

    [Fact]
    public void SendingBackStartsANewAttemptWithTheFeedback()
    {
        var job = Given.JobIn(JobState.AwaitingReview);

        var started = Outcomes.Succeeds(job.SendBack(Given.Feedback));

        Assert.Equal(new AttemptStarted(job.Id, new AttemptNumber(2), AttemptOrigin.SendBack, Given.Feedback), started);
        Assert.Equal(JobState.Running, job.State);
    }

    [Fact]
    public void DiscardingInterruptsTheAttemptUnderway()
    {
        var job = Given.JobIn(JobState.Running);

        Assert.Equal(new JobDiscarded(job.Id, JobState.Running), Outcomes.Succeeds(job.Discard()));
        Assert.Equal(AttemptOutcome.Interrupted, job.Attempts[^1].Outcome);
    }

    [Fact]
    public void DiscardingKeepsTheOutcomeOfAFinishedAttempt()
    {
        var job = Given.JobIn(JobState.AwaitingReview);

        Outcomes.Succeeds(job.Discard());

        Assert.Equal(AttemptOutcome.Passed, job.Attempts[^1].Outcome);
    }

    [Fact]
    public void FailingRecordsTheReasonAndInterruptsTheAttempt()
    {
        var job = Given.JobIn(JobState.Checking);

        Assert.Equal(new JobFailed(job.Id, FailureReason.AgentUnavailable), Outcomes.Succeeds(job.Fail(FailureReason.AgentUnavailable)));
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(AttemptOutcome.Interrupted, job.Attempts[^1].Outcome);
    }

    [Fact]
    public void HoldingARunningJobRecordsTheReasonInterruptsTheAttemptAndAsksForHelp()
    {
        var job = Given.JobIn(JobState.Running);

        Assert.Equal(new JobHeld(job.Id, AttemptNumber.First, HoldReason.Stalled), Outcomes.Succeeds(job.Hold(HoldReason.Stalled)));
        Assert.Equal(JobState.NeedsHelp, job.State);
        Assert.Equal(AttemptOutcome.Interrupted, Assert.Single(job.Attempts).Outcome);
    }
}
