using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Tests.Coordination;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Jobs.Tests.Holding;

public sealed class ChooseModelTests
{
    private static readonly ResumeToken Token = new("conversation-1");

    private static readonly ModelChoice Opus = new("opus", "high");

    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task AModelChosenForAHeldJobResumesItsConversationWithThatModelAtTheNextMessageAsync()
    {
        var flow = Offering(new FakeAgents { Resumes = true });
        var job = await flow.HeldAsync(HoldReason.Interrupted, Token);
        var idle = Outcomes.Present(job.Session);

        var chosen = Outcomes.Succeeds(await flow.Jobs.ChooseModelAsync(job.Id, Opus, Cancellation));
        var stopped = flow.Agents.Stopped.Contains(idle);
        var continued = Outcomes.Succeeds(await flow.Jobs.ContinueAsync(job.Id, "Go on", Cancellation));

        Assert.Equal(new JobModelChosen(job.Id, Opus), chosen);
        Assert.Equal(chosen, flow.Bus.Published.OfType<JobModelChosen>().Single());
        Assert.True(stopped);
        Assert.Equal(ContinuedIn.ResumedConversation, continued.Conversation);
        Assert.Equal((Opus, Option<ResumeToken>.Some(Token)), (flow.Agents.Requests[^1].Model, flow.Agents.Requests[^1].Resume));
        Assert.Equal(Opus, Outcomes.Present(await flow.Catalog.HistoryAsync(job.Id, Cancellation)).Summary.Model);
    }

    [Fact]
    public async Task AModelIsChosenOnlyForAnIdleJobWhoseConversationResumesOnAConnectionThatOffersItAsync()
    {
        var flow = Offering(new FakeAgents { Resumes = true });
        var running = await flow.RunningAsync();
        var unresumable = await flow.HeldAsync(HoldReason.Interrupted);
        var held = await flow.HeldAsync(HoldReason.Interrupted, Token);

        Assert.Equal(JobRejection.NotHeld, Outcomes.FailsWith(await flow.Jobs.ChooseModelAsync(running.Id, Opus, Cancellation)));
        Assert.Equal(JobRejection.NotResumable, Outcomes.FailsWith(await flow.Jobs.ChooseModelAsync(unresumable.Id, Opus, Cancellation)));
        Assert.Equal(JobRejection.UnofferedModel, Outcomes.FailsWith(await flow.Jobs.ChooseModelAsync(held.Id, new ModelChoice("huge", "high"), Cancellation)));
        Assert.Equal(JobRejection.UnofferedEffort, Outcomes.FailsWith(await flow.Jobs.ChooseModelAsync(held.Id, new ModelChoice("opus", "max"), Cancellation)));
        Assert.Equal(JobRejection.UnknownJob, Outcomes.FailsWith(await flow.Jobs.ChooseModelAsync(JobId.New(), Opus, Cancellation)));
        Assert.All([running, unresumable, held], job => Assert.Equal(ModelChoice.Default, job.Model));
        Assert.Empty(flow.Bus.Published.OfType<JobModelChosen>());
        Assert.Empty(flow.Agents.Stopped);
    }

    private static JobFlow Offering(FakeAgents agents)
    {
        var flow = new JobFlow(new FakeWorkspaces(), agents);
        flow.Connections.Offers[FakeAgents.DefaultConnection.Value] = CapabilitySet.None.With(new OffersModels(["sonnet", "opus"], ["low", "high"]));

        return flow;
    }
}
