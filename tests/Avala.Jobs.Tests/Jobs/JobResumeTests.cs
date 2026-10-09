using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Avala.Testing;
using AttemptOrigin = Avala.Jobs.Contracts.AttemptOrigin;

namespace Avala.Jobs.Tests.Jobs;

public sealed class JobResumeTests
{
    private static readonly ResumeToken Token = new("conversation-1");

    [Fact]
    public void OnlyTheJobsCurrentSessionRecordsWhereItsConversationCanResume()
    {
        var job = Given.JobIn(JobState.Running);

        Assert.Equal(JobError.ForeignSession, Outcomes.FailsWith(job.RecordResume(SessionId.New(), Token)));
        Assert.Equal(Option<ResumeToken>.None, job.Resume);
        Assert.Equal(new ResumeRecorded(job.Id, Given.Session), Outcomes.Succeeds(job.RecordResume(Given.Session, Token)));
        Assert.Equal(Option<ResumeToken>.Some(Token), job.Resume);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RecoveringKeepsTheResumeTokenOnlyWhenTheNewSessionResumedTheConversation(bool resumed)
    {
        var job = Given.JobIn(JobState.Running);
        Outcomes.Succeeds(job.RecordResume(Given.Session, Token));

        Outcomes.Succeeds(job.Recover(SessionId.New(), resumed));

        Assert.Equal(resumed ? Option<ResumeToken>.Some(Token) : Option<ResumeToken>.None, job.Resume);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void ANewRoundInANewSessionRunsTheJobThereAndKeepsTheResumeTokenOnlyWhenItResumed(bool sentBack, bool resumed)
    {
        var job = Given.JobIn(sentBack ? JobState.AwaitingReview : JobState.NeedsHelp);
        Outcomes.Succeeds(job.RecordResume(Given.Session, Token));
        var session = SessionId.New();

        var started = Outcomes.Succeeds(sentBack
            ? job.SendBack(Given.Feedback, session, resumed)
            : job.Hint(Given.Feedback, session, resumed));

        Assert.Equal(
            (sentBack ? AttemptOrigin.SendBack : AttemptOrigin.Hint, Option<Feedback>.Some(Given.Feedback)),
            (started.Origin, started.Guidance));
        Assert.Equal((Option<SessionId>.Some(session), JobState.Running), (job.Session, job.State));
        Assert.Equal(Option<SessionId>.Some(session), job.Attempts[^1].Session);
        Assert.Equal(resumed ? Option<ResumeToken>.Some(Token) : Option<ResumeToken>.None, job.Resume);
    }
}
