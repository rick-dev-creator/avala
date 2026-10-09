using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Tests.Coordination;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Jobs.Tests.Holding;

public sealed class ContinueJobTests
{
    private const string Message = "Use the staging database";

    private static readonly ResumeToken Token = new("conversation-1");

    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task ContinuingAHeldJobWhoseSessionIsOpenSendsTheMessageToThatSessionAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.HeldAsync(HoldReason.Stalled);
        var session = Outcomes.Present(job.Session);

        var continued = Outcomes.Succeeds(await flow.Jobs.ContinueAsync(job.Id, Message, Cancellation));

        Assert.Equal(new JobContinuation(job.Id, session, ContinuedIn.SameSession), continued);
        Assert.Equal((JobState.Running, AttemptOrigin.Hint), (job.State, job.Attempts[^1].Origin));
        Assert.Equal((session, Message), flow.Agents.Sent[^1]);
        Assert.Single(flow.Agents.Requests);
    }

    [Fact]
    public async Task ContinuingAJobWhoseSessionWasLostResumesItsConversationInANewSessionAsync()
    {
        var flow = new JobFlow(new FakeWorkspaces(), new FakeAgents { Resumes = true });
        var job = await flow.HeldAsync(HoldReason.SessionLost, Token);

        var continued = Outcomes.Succeeds(await flow.Jobs.ContinueAsync(job.Id, Message, Cancellation));

        var session = Outcomes.Present(job.Session);
        Assert.Equal(new JobContinuation(job.Id, session, ContinuedIn.ResumedConversation), continued);
        Assert.Equal(Option<ResumeToken>.Some(Token), flow.Agents.Requests[^1].Resume);
        Assert.Equal((session, Message), flow.Agents.Sent[^1]);
        Assert.Equal(new JobSessionStarted(job.Id, session), flow.Bus.Published.OfType<JobSessionStarted>().Last());
        Assert.Equal((JobState.Running, AttemptOrigin.Hint), (job.State, job.Attempts[^1].Origin));
    }

    [Fact]
    public async Task ContinuingAJobWhoseConversationCannotResumeStartsOverWithTheInstructionAndTheMessageAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.HeldAsync(HoldReason.SessionLost, Token);

        var continued = Outcomes.Succeeds(await flow.Jobs.ContinueAsync(job.Id, Message, Cancellation));

        Assert.Equal(ContinuedIn.NewConversation, continued.Conversation);
        Assert.Equal((continued.Session, $"Add GitHub login\n\n{Message}"), flow.Agents.Sent[^1]);
        Assert.Equal(Option<ResumeToken>.None, job.Resume);
    }

    [Fact]
    public async Task AJobWhoseSessionCannotOpenAgainStaysHeldAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.HeldAsync(HoldReason.SessionLost);
        flow.Agents.OpeningFails = true;

        Assert.Equal(JobRejection.AgentUnavailable, Outcomes.FailsWith(await flow.Jobs.ContinueAsync(job.Id, Message, Cancellation)));
        Assert.Equal(JobState.NeedsHelp, job.State);
    }

    [Fact]
    public async Task OnlyAHeldJobCanBeContinuedAndOnlyWithAMessageAsync()
    {
        var flow = JobFlow.With();
        var running = await flow.RunningAsync();
        var held = await flow.HeldAsync(HoldReason.Stalled);
        var sent = flow.Agents.Sent.Count;

        Assert.Equal(JobRejection.NotHeld, Outcomes.FailsWith(await flow.Jobs.ContinueAsync(running.Id, Message, Cancellation)));
        Assert.Equal(JobRejection.UnknownJob, Outcomes.FailsWith(await flow.Jobs.ContinueAsync(JobId.New(), Message, Cancellation)));
        Assert.Equal(JobRejection.EmptyMessage, Outcomes.FailsWith(await flow.Jobs.ContinueAsync(held.Id, " ", Cancellation)));
        Assert.Equal((JobState.Running, JobState.NeedsHelp), (running.State, held.State));
        Assert.Equal(sent, flow.Agents.Sent.Count);
    }
}
