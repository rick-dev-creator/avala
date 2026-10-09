using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Jobs.Jobs;
using Avala.Testing;
using AttemptOrigin = Avala.Jobs.Contracts.AttemptOrigin;
using AttemptOutcome = Avala.Jobs.Contracts.AttemptOutcome;

namespace Avala.Jobs.Tests.Jobs;

public sealed class JobRetryTests
{
    [Fact]
    public void RetryingRejectsTheAttemptAndStartsTheNext()
    {
        var job = Given.JobIn(JobState.Checking);

        var retried = Outcomes.Succeeds(job.Retry(Given.Feedback));

        Assert.Equal(new AttemptRetried(job.Id, AttemptNumber.First, new AttemptNumber(2), Given.Feedback), retried);
        Assert.Equal([AttemptOutcome.Rejected, AttemptOutcome.Running], job.Attempts.Select(attempt => attempt.Outcome));
        Assert.Equal(AttemptOrigin.Retry, job.Attempts[^1].Origin);
    }

    [Fact]
    public void RetryingInANewSessionStartsTheNextAttemptThere()
    {
        var job = Given.JobIn(JobState.Checking);
        var session = SessionId.New();

        var retried = Outcomes.Succeeds(job.Retry(Given.Feedback, session, resumed: true));

        Assert.Equal(new AttemptRetried(job.Id, AttemptNumber.First, new AttemptNumber(2), Given.Feedback), retried);
        Assert.Equal([Option<SessionId>.Some(Given.Session), Option<SessionId>.Some(session)], job.Attempts.Select(attempt => attempt.Session));
        Assert.Equal((JobState.Running, Option<SessionId>.Some(session)), (job.State, job.Session));
    }

    [Fact]
    public void RetriesStopOnceTheRoundBudgetIsSpent()
    {
        var job = Given.JobIn(JobState.Checking, attemptsPerRound: 2);
        Outcomes.Succeeds(job.Retry(Given.Feedback));
        Outcomes.Succeeds(job.CompleteTurn());

        Assert.Equal(JobError.AttemptBudgetExhausted, Outcomes.FailsWith(job.Retry(Given.Feedback)));
        Assert.Equal(JobState.Checking, job.State);
    }

    [Fact]
    public void RequestingHelpRejectsTheLastAttempt()
    {
        var job = Given.JobIn(JobState.Checking, attemptsPerRound: 1);

        Assert.Equal(new HelpRequested(job.Id, AttemptNumber.First), Outcomes.Succeeds(job.RequestHelp()));
        Assert.Equal(JobState.NeedsHelp, job.State);
        Assert.Equal(AttemptOutcome.Rejected, job.Attempts[^1].Outcome);
    }

    [Fact]
    public void AHintStartsAFreshRoundOfRetries()
    {
        var job = Given.JobIn(JobState.Checking, attemptsPerRound: 2);
        Outcomes.Succeeds(job.Retry(Given.Feedback));
        Outcomes.Succeeds(job.CompleteTurn());
        Outcomes.Succeeds(job.RequestHelp());

        var hinted = Outcomes.Succeeds(job.Hint(Given.Feedback));
        Outcomes.Succeeds(job.CompleteTurn());

        Assert.Equal(new AttemptStarted(job.Id, new AttemptNumber(3), AttemptOrigin.Hint, Given.Feedback), hinted);
        Assert.True(job.Retry(Given.Feedback).IsSuccess);
    }

    [Fact]
    public void SendingBackStartsAFreshRoundOfRetries()
    {
        var job = Given.JobIn(JobState.AwaitingReview, attemptsPerRound: 1);
        Outcomes.Succeeds(job.SendBack(Given.Feedback));
        Outcomes.Succeeds(job.CompleteTurn());

        Assert.Equal(JobError.AttemptBudgetExhausted, Outcomes.FailsWith(job.Retry(Given.Feedback)));
        Assert.True(job.RequestHelp().IsSuccess);
    }

    [Fact]
    public void RecoveryStartsAFreshRoundOfRetries()
    {
        var job = Given.JobIn(JobState.Checking, attemptsPerRound: 2);
        Outcomes.Succeeds(job.Retry(Given.Feedback));

        Outcomes.Succeeds(job.Recover(Given.Session, resumed: false));
        Outcomes.Succeeds(job.CompleteTurn());

        Assert.True(job.Retry(Given.Feedback).IsSuccess);
    }
}
