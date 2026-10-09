using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Launching;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Jobs.Tests.Coordination;

public sealed class DeferredRecoveryTests
{
    private const string Note = "Your sub-agent reported: integrated.";

    private static readonly ResumeToken Token = new("conversation-1");

    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task RecoveryLeavesARunningJobADeferralAsksForInItsOldSessionAndRelaunchesTheOthersAsync()
    {
        var flow = JobFlow.With();
        var waiting = await flow.RunningAsync();
        var other = await flow.RunningAsync();
        var (old, otherOld) = (waiting.Session, other.Session);
        flow.Deferrals.Add(new Deferring(waiting.Id));

        await flow.Recovery.RunAsync(Cancellation);

        Assert.Equal((JobState.Running, old), (waiting.State, waiting.Session));
        Assert.Equal([AttemptOrigin.Initial], waiting.Attempts.Select(attempt => attempt.Origin));
        Assert.NotEqual(otherOld, other.Session);
        Assert.Equal(3, flow.Agents.Sessions.Count);
    }

    [Fact]
    public async Task ResumingADeferredJobResumesItsConversationWithTheRestartNoteAndEveryBriefingAsync()
    {
        var flow = new JobFlow(new FakeWorkspaces(), new FakeAgents { Resumes = true });
        var job = await DeferredAsync(flow, Option<ResumeToken>.Some(Token));
        flow.Briefings.Add(new Briefing(job.Id, Note));

        var resumed = Outcomes.Succeeds(await flow.Jobs.ResumeAsync(job.Id, Cancellation));

        var session = Outcomes.Present(job.Session);
        Assert.Equal(new JobContinuation(job.Id, session, ContinuedIn.ResumedConversation), resumed);
        Assert.Equal(Option<ResumeToken>.Some(Token), flow.Agents.Requests[^1].Resume);
        Assert.Equal((session, $"{JobLauncher.RestartNote}\n\n{Note}"), flow.Agents.Sent[^1]);
        Assert.Equal((JobState.Running, AttemptOrigin.Recovery), (job.State, job.Attempts[^1].Origin));
        Assert.Equal(new JobSessionStarted(job.Id, session), flow.Bus.Published.OfType<JobSessionStarted>().Last());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ADeferredJobWhoseConversationCannotResumeIsHeldAsNotResumableAndToldNothingAsync(bool hasToken)
    {
        var flow = JobFlow.With();
        var job = await DeferredAsync(flow, hasToken ? Option<ResumeToken>.Some(Token) : Option<ResumeToken>.None);
        var told = flow.Agents.Sent.Count;
        flow.Briefings.Add(new Briefing(job.Id, Note));

        Assert.Equal(JobRejection.NotResumable, Outcomes.FailsWith(await flow.Jobs.ResumeAsync(job.Id, Cancellation)));

        Assert.Equal(JobState.NeedsHelp, job.State);
        Assert.Equal(HoldReason.NotResumable, flow.Bus.Published.OfType<Contracts.JobHeld>().Single().Hold.Reason);
        Assert.Equal(told, flow.Agents.Sent.Count);
        Assert.Equal(hasToken ? 1 : 0, flow.Agents.Stopped.Count);
        Assert.Equal(0, flow.Briefings.OfType<Briefing>().Single().Asked);
    }

    [Fact]
    public async Task AJobRecoveryDidNotDeferIsNotResumedAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();
        var session = job.Session;

        Assert.Equal(JobRejection.NotDeferred, Outcomes.FailsWith(await flow.Jobs.ResumeAsync(job.Id, Cancellation)));

        Assert.Equal((JobState.Running, session), (job.State, job.Session));
        Assert.Single(flow.Agents.Requests);
    }

    [Fact]
    public async Task ADeferredJobIsResumedOnlyOnceAsync()
    {
        var flow = new JobFlow(new FakeWorkspaces(), new FakeAgents { Resumes = true });
        var job = await DeferredAsync(flow, Option<ResumeToken>.Some(Token));
        _ = Outcomes.Succeeds(await flow.Jobs.ResumeAsync(job.Id, Cancellation));
        var session = job.Session;

        Assert.Equal(JobRejection.NotDeferred, Outcomes.FailsWith(await flow.Jobs.ResumeAsync(job.Id, Cancellation)));

        Assert.Equal(session, job.Session);
    }

    [Fact]
    public async Task AHeldJobContinuedByAPersonIsBriefedAfterTheMessageAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.HeldAsync(HoldReason.Stalled);
        flow.Briefings.Add(new Briefing(job.Id, Note));

        var continued = Outcomes.Succeeds(await flow.Jobs.ContinueAsync(job.Id, "Go on", Cancellation));

        Assert.Equal((continued.Session, $"Go on\n\n{Note}"), flow.Agents.Sent[^1]);
    }

    private static async Task<Job> DeferredAsync(JobFlow flow, Option<ResumeToken> token)
    {
        var job = await flow.RunningAsync();
        await token.Match(resume => flow.OfferResumeAsync(job, Outcomes.Present(job.Session), resume), () => Task.CompletedTask);
        flow.Deferrals.Add(new Deferring(job.Id));
        await flow.Recovery.RunAsync(Cancellation);

        return job;
    }

    private sealed class Deferring(JobId deferred) : IRecoveryDeferral
    {
        public ValueTask<bool> DefersAsync(JobId job, CancellationToken cancellationToken) => ValueTask.FromResult(job == deferred);
    }

    private sealed class Briefing(JobId briefed, string note) : IJobBriefing
    {
        public int Asked { get; private set; }

        public ValueTask<Option<string>> BriefAsync(JobId job, CancellationToken cancellationToken)
        {
            Asked++;

            return ValueTask.FromResult(job == briefed ? note : Option<string>.None);
        }
    }
}
