using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Tests.Coordination;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Jobs.Tests.Holding;

public sealed class RemoteControlTests
{
    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task TurningRemoteControlOnForAJobItsPersonStartedRecordsItAndAsksItsSessionAsync()
    {
        var flow = Controllable();
        var job = await flow.RunningAsync();

        var chosen = Outcomes.Succeeds(await flow.Jobs.ControlRemotelyAsync(job.Id, on: true, Cancellation));

        Assert.Equal(new JobRemoteControlChosen(job.Id, true), chosen);
        Assert.Equal(chosen, flow.Bus.Published.OfType<JobRemoteControlChosen>().Single());
        Assert.Equal([(Outcomes.Present(job.Session), true)], flow.Agents.RemoteControls);
        Assert.True(Outcomes.Present(await flow.Catalog.HistoryAsync(job.Id, Cancellation)).Summary.RemoteControlled);
    }

    [Fact]
    public async Task OnlyAJobItsPersonStartedOnAConnectionThatOffersItCanBeControlledRemotelyAsync()
    {
        var flow = Controllable();
        var parent = await flow.RunningAsync();
        var child = await flow.RunningAsync(JobFlow.Request() with { Instruction = "Write the notes", Parent = parent.Id });
        var plain = JobFlow.With();
        var elsewhere = await plain.RunningAsync();

        Assert.Equal(JobRejection.NotRoot, Outcomes.FailsWith(await flow.Jobs.ControlRemotelyAsync(child.Id, on: true, Cancellation)));
        Assert.Equal(JobRejection.NotRemoteControllable, Outcomes.FailsWith(await plain.Jobs.ControlRemotelyAsync(elsewhere.Id, on: true, Cancellation)));
        Assert.Equal(JobRejection.UnknownJob, Outcomes.FailsWith(await flow.Jobs.ControlRemotelyAsync(JobId.New(), on: true, Cancellation)));
        Assert.Empty(flow.Agents.RemoteControls);
        Assert.Empty(plain.Agents.RemoteControls);
        Assert.False(child.RemoteControlled);
    }

    [Fact]
    public async Task WhereTheProviderStartsJobsOnThePhoneEveryJobItsPersonStartsBeginsControlledRemotelyAndNoDelegatedJobDoesAsync()
    {
        var flow = Controllable(startsOn: true);
        var parent = await flow.RunningAsync();

        var child = await flow.RunningAsync(JobFlow.Request() with { Instruction = "Write the notes", Parent = parent.Id });

        Assert.True(parent.RemoteControlled);
        Assert.False(child.RemoteControlled);
        Assert.Equal([(Outcomes.Present(parent.Session), true)], flow.Agents.RemoteControls);
    }

    [Fact]
    public async Task AHeldJobIsNotControlledRemotelyUntilItRunsAgainAsync()
    {
        var flow = Controllable();
        var job = await flow.RunningAsync();
        Outcomes.Succeeds(await flow.Jobs.ControlRemotelyAsync(job.Id, on: true, Cancellation));
        var session = Outcomes.Present(job.Session);

        Outcomes.Succeeds(await flow.Jobs.HoldAsync(job.Id, HoldReason.BudgetExceeded, Cancellation));
        var held = flow.Agents.RemoteControls.Last();
        Outcomes.Succeeds(await flow.Jobs.ContinueAsync(job.Id, "The budget is raised, go on", Cancellation));

        Assert.Equal((session, false), held);
        Assert.Equal((Outcomes.Present(job.Session), true), flow.Agents.RemoteControls.Last());
    }

    [Fact]
    public async Task AMessageSentFromADeviceToAJobAwaitingReviewContinuesItAsYourOwnMessageWouldAsync()
    {
        var flow = Controllable();
        var job = await flow.RunningAsync();
        await flow.FinishTurnAsync(job);
        var session = Outcomes.Present(job.Session);

        await flow.Check.HandleAsync(new RemoteMessageReceived(session, TurnId.New(), "Also cover partial refunds"), Cancellation);
        await flow.SettledAsync(job);
        var unmoved = job.State;
        await flow.Check.HandleAsync(new RemoteMessageReceived(session, TurnId.New(), "Also cover partial refunds") { BeganTurn = true }, Cancellation);
        await flow.SettledAsync(job);

        Assert.Equal(JobState.AwaitingReview, unmoved);
        Assert.Equal(JobState.Running, job.State);
        Assert.Equal((AttemptOrigin.Device, Option<string>.Some("Also cover partial refunds")), (job.Attempts[^1].Origin, job.Attempts[^1].Guidance.Map(guidance => guidance.Text)));
        Assert.Equal(Option<SessionId>.Some(session), job.Session);
        Assert.DoesNotContain(flow.Agents.Sent, sent => sent.Message == "Also cover partial refunds");
    }

    private static JobFlow Controllable(bool startsOn = false)
    {
        var flow = JobFlow.With();
        var remote = CapabilitySet.None.With(new ControlsRemotely { StartsOn = startsOn });
        flow.Connections.Offers[FakeAgents.DefaultConnection.Value] = remote;
        flow.Agents.Capabilities = remote;

        return flow;
    }
}
