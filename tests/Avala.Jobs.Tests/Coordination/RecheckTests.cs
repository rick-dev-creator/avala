using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Jobs.Tests.Coordination;

public sealed class RecheckTests
{
    private static readonly ResumeToken Token = new("conversation-1");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobLeftCheckingRunsItsChecksAgainForTheSameAttemptWithoutTellingTheAgentAsync()
    {
        var gate = new ScriptedGate();
        var flow = JobFlow.With(gate);
        var job = await CheckingAsync(flow);

        await flow.Recovery.RunAsync(Cancellation);

        Assert.Equal(JobState.AwaitingReview, job.State);
        Assert.Equal(1, Assert.Single(gate.Evaluated).Attempt);
        Assert.Equal(AttemptOutcome.Passed, Assert.Single(job.Attempts).Outcome);
        Assert.Single(flow.Agents.Sessions);
        Assert.Equal(["Add GitHub login"], flow.Agents.Sent.Select(sent => sent.Message));
        Assert.Contains(new JobProgressed(job.Id, JobStatus.Checking), flow.Bus.Published);
    }

    [Theory]
    [InlineData(true, "Two tests fail")]
    [InlineData(false, "Add GitHub login\n\nTwo tests fail")]
    public async Task AFailedRecheckRetriesInANewSessionThatResumesTheConversationWhenItCanAsync(bool resumes, string told)
    {
        var flow = new JobFlow(new FakeWorkspaces(), new FakeAgents { Resumes = resumes }, new ScriptedGate(GateVerdict.Retry("Two tests fail")));
        var job = await CheckingAsync(flow);
        var lost = job.Session;

        await flow.Recovery.RunAsync(Cancellation);

        var session = Outcomes.Present(job.Session);
        Assert.NotEqual(lost, job.Session);
        Assert.Equal(Option<ResumeToken>.Some(Token), flow.Agents.Requests[^1].Resume);
        Assert.Equal(JobState.Running, job.State);
        Assert.Equal(
            [(AttemptOrigin.Initial, AttemptOutcome.Rejected, lost), (AttemptOrigin.Retry, AttemptOutcome.Running, Option<SessionId>.Some(session))],
            job.Attempts.Select(attempt => (attempt.Origin, attempt.Outcome, attempt.Session)));
        Assert.Equal((session, told), flow.Agents.Sent[^1]);
    }

    [Fact]
    public async Task AFailedRecheckWithNoRetryLeftAsksForHelpWithoutOpeningASessionAsync()
    {
        var flow = JobFlow.With(new ScriptedGate(GateVerdict.Retry("Two tests fail")));
        var job = await CheckingAsync(flow, attemptsPerRound: 1);

        await flow.Recovery.RunAsync(Cancellation);

        Assert.Equal(JobState.NeedsHelp, job.State);
        Assert.Equal(AttemptOutcome.Rejected, Assert.Single(job.Attempts).Outcome);
        Assert.Single(flow.Agents.Sessions);
    }

    [Fact]
    public async Task ADeferredVerdictLeavesTheJobCheckingWithoutJudgingItOrTellingTheAgentAsync()
    {
        var flow = JobFlow.With(new ScriptedGate(GateVerdict.Deferred));
        var job = await flow.RunningAsync();

        await flow.FinishTurnAsync(job);

        Assert.Equal(JobState.Checking, job.State);
        Assert.Equal(AttemptOutcome.AwaitingCheck, Assert.Single(job.Attempts).Outcome);
        Assert.Equal(["Add GitHub login"], flow.Agents.Sent.Select(sent => sent.Message));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ARecheckJudgesADeferredJobAgainAndRetriesItInItsOpenSessionAsync(bool passes)
    {
        var state = passes ? JobState.AwaitingReview : JobState.Running;
        var gate = new ScriptedGate(GateVerdict.Deferred, passes ? GateVerdict.Pass : GateVerdict.Retry("Two tests fail"));
        var flow = JobFlow.With(gate);
        var job = await flow.RunningAsync();
        await flow.FinishTurnAsync(job);

        await flow.Rechecks.HandleAsync(new DeferredGateReady(job.Id), Cancellation);
        await flow.SettledAsync(job);

        Assert.Equal(state, job.State);
        Assert.Equal([1, 1], gate.Evaluated.Select(attempt => attempt.Attempt));
        Assert.Single(flow.Agents.Sessions);
        Assert.Equal(passes ? "Add GitHub login" : "Two tests fail", flow.Agents.Sent[^1].Message);
    }

    [Fact]
    public async Task ARecheckOfAJobThatIsNotCheckingChangesNothingAsync()
    {
        var gate = new ScriptedGate();
        var flow = JobFlow.With(gate);
        var job = await flow.RunningAsync();

        await flow.Rechecks.HandleAsync(new DeferredGateReady(job.Id), Cancellation);
        await flow.SettledAsync(job);

        Assert.Equal(JobState.Running, job.State);
        Assert.Empty(gate.Evaluated);
    }

    private static async Task<Job> CheckingAsync(JobFlow flow, int attemptsPerRound = 3)
    {
        var job = await flow.RunningAsync(attemptsPerRound);
        await flow.OfferResumeAsync(job, Outcomes.Present(job.Session), Token);
        Outcomes.Succeeds(job.CompleteTurn());

        return job;
    }
}
