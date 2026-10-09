using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Launching;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Jobs.Tests.Coordination;

public sealed class ResumingTests
{
    private static readonly ResumeToken Token = new("conversation-1");

    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task ARunningJobStoresTheResumeTokenOfItsSessionAndAnnouncesItAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();
        var session = Outcomes.Present(job.Session);

        await flow.OfferResumeAsync(job, session, Token);

        Assert.Equal(Option<ResumeToken>.Some(Token), flow.Store.Jobs.Single().Resume);
        Assert.Equal(new JobResumable(job.Id, session), flow.Bus.Published[^1]);
    }

    [Fact]
    public async Task AResumeTokenFromASessionTheJobDoesNotUseIsIgnoredAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();
        var lost = Outcomes.Present(job.Session);
        await flow.Recovery.RunAsync(Cancellation);

        await flow.OfferResumeAsync(job, lost, Token);

        Assert.Equal(Option<ResumeToken>.None, job.Resume);
        Assert.DoesNotContain(flow.Bus.Published, published => published is JobResumable);
    }

    [Fact]
    public async Task RecoveryResumesTheConversationOfTheJobAndTellsTheAgentToContinueAsync()
    {
        var flow = new JobFlow(new FakeWorkspaces(), new FakeAgents { Resumes = true });
        var job = await flow.RunningAsync();
        await flow.OfferResumeAsync(job, Outcomes.Present(job.Session), Token);

        await flow.Recovery.RunAsync(Cancellation);

        var session = Outcomes.Present(job.Session);
        Assert.Equal(Option<ResumeToken>.Some(Token), flow.Agents.Requests[^1].Resume);
        Assert.Equal((session, JobLauncher.RestartNote), flow.Agents.Sent[^1]);
        Assert.Equal(Option<ResumeToken>.Some(Token), job.Resume);
    }

    [Fact]
    public async Task RecoveryStartsOverWithTheInstructionWhenTheConversationIsNotResumedAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();
        await flow.OfferResumeAsync(job, Outcomes.Present(job.Session), Token);

        await flow.Recovery.RunAsync(Cancellation);

        Assert.Equal(Option<ResumeToken>.Some(Token), flow.Agents.Requests[^1].Resume);
        Assert.Equal((Outcomes.Present(job.Session), "Add GitHub login"), flow.Agents.Sent[^1]);
        Assert.Equal(Option<ResumeToken>.None, job.Resume);
    }
}
