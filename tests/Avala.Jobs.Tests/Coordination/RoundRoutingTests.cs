using System.Collections.Concurrent;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Jobs.Tests.Coordination;

public sealed class RoundRoutingTests
{
    private const string Brief = "This job was handed off to you from default.";

    private static readonly ConnectionName Personal = new("personal");

    private static readonly ConnectionChoice ToPersonal = Choice(Personal);

    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task ARouterIsAskedWithTheJobItsConnectionAndTheFeedbackBeforeARetryAsync()
    {
        var router = new ScriptedRouter(RoundRoute.GoOn);
        var flow = Routed(router);
        var job = await flow.RunningAsync();

        await flow.FinishTurnAsync(job);

        Assert.Equal(new RoundQuestion(job.Id, FakeAgents.DefaultConnection, "Two tests fail"), Assert.Single(router.Asked));
        Assert.Equal(["Add GitHub login", "Two tests fail"], flow.Agents.Sent.Select(sent => sent.Message));
    }

    [Fact]
    public async Task AHandoffAtTheEndOfATurnStartsANewConversationOnTheChosenConnectionWithTheBriefAsync()
    {
        var flow = Routed(new ScriptedRouter(RoundRoute.To(new JobHandoff(ToPersonal, Brief))));
        var job = await flow.RunningAsync();
        var old = Outcomes.Present(job.Session);

        await flow.FinishTurnAsync(job);

        var session = Outcomes.Present(job.Session);
        Assert.NotEqual(old, session);
        Assert.Equal((Option<ConnectionName>.Some(Personal), Option<ResumeToken>.None), (flow.Agents.Requests[^1].Connection, flow.Agents.Requests[^1].Resume));
        Assert.Equal((session, Brief), flow.Agents.Sent[^1]);
        Assert.Equal(
            [(AttemptOrigin.Initial, AttemptOutcome.Rejected), (AttemptOrigin.Handoff, AttemptOutcome.Running)],
            job.Attempts.Select(attempt => (attempt.Origin, attempt.Outcome)));
        Assert.Equal((JobState.Running, Option<ConnectionName>.Some(Personal)), (job.State, job.Connection));
        Assert.Contains(old, flow.Agents.Stopped);
        Assert.Equal(ToPersonal, flow.Store.Choices[job.Id]);
        Assert.Equal(new JobHandedOff(job.Id, FakeAgents.DefaultConnection, Personal, session, 2, ToPersonal), flow.Bus.Published.OfType<JobHandedOff>().Single());
        Assert.Equal(new ConnectionChosen(job.Id, ToPersonal), flow.Bus.Published.OfType<ConnectionChosen>().Single());
    }

    [Fact]
    public async Task AHoldAtTheEndOfATurnKeepsTheSessionIdleAndSendsNothingAsync()
    {
        var flow = Routed(new ScriptedRouter(RoundRoute.Held(HoldReason.LimitNearlyReached)));
        var job = await flow.RunningAsync();
        var session = Outcomes.Present(job.Session);

        await flow.FinishTurnAsync(job);

        Assert.Equal(JobState.NeedsHelp, job.State);
        Assert.Equal(
            [(AttemptOrigin.Initial, AttemptOutcome.Rejected), (AttemptOrigin.Retry, AttemptOutcome.Interrupted)],
            job.Attempts.Select(attempt => (attempt.Origin, attempt.Outcome)));
        Assert.Equal(["Add GitHub login"], flow.Agents.Sent.Select(sent => sent.Message));
        Assert.Equal(HoldReason.LimitNearlyReached, flow.Bus.Published.OfType<Contracts.JobHeld>().Single().Hold.Reason);
        Assert.DoesNotContain(session, flow.Agents.Stopped);
    }

    [Fact]
    public async Task AHandoffWhoseConnectionCannotOpenHoldsTheJobInsteadAsync()
    {
        var flow = Routed(new ScriptedRouter(RoundRoute.To(new JobHandoff(Choice(new ConnectionName("nowhere")), Brief))));
        flow.Agents.UnknownConnections.Add("nowhere");
        var job = await flow.RunningAsync();

        await flow.FinishTurnAsync(job);

        Assert.Equal((JobState.NeedsHelp, Option<ConnectionName>.Some(FakeAgents.DefaultConnection)), (job.State, job.Connection));
        Assert.Equal(HoldReason.LimitNearlyReached, flow.Bus.Published.OfType<Contracts.JobHeld>().Single().Hold.Reason);
    }

    [Fact]
    public async Task AJobWithoutRetriesLeftAsksForHelpWithoutAskingTheRouterAsync()
    {
        var router = new ScriptedRouter(RoundRoute.To(new JobHandoff(ToPersonal, Brief)));
        var flow = Routed(router);
        var job = await flow.RunningAsync(attemptsPerRound: 1);

        await flow.FinishTurnAsync(job);

        Assert.Equal(JobState.NeedsHelp, job.State);
        Assert.Empty(router.Asked);
    }

    [Fact]
    public async Task AHeldJobIsHandedOffAsANewConversationThatBeginsWithTheBriefAsync()
    {
        var flow = new JobFlow(new FakeWorkspaces(), new FakeAgents { Resumes = true });
        var job = await flow.HeldAsync(HoldReason.LimitNearlyReached, new ResumeToken("conversation-1"));
        var old = Outcomes.Present(job.Session);

        var continued = Outcomes.Succeeds(await flow.Jobs.HandOffAsync(job.Id, new JobHandoff(ToPersonal, Brief), Cancellation));

        Assert.Equal(new JobContinuation(job.Id, Outcomes.Present(job.Session), ContinuedIn.NewConversation), continued);
        Assert.Equal((continued.Session, Brief), flow.Agents.Sent[^1]);
        Assert.Equal((AttemptOrigin.Handoff, Option<ResumeToken>.None), (job.Attempts[^1].Origin, job.Resume));
        Assert.Contains(old, flow.Agents.Stopped);
        Assert.Single(flow.Bus.Published.OfType<JobHandedOff>());
    }

    [Fact]
    public async Task AHandoffToAConnectionThatDoesNotOfferTheJobsModelRunsWithItsDefaultInsteadOfFailingAsync()
    {
        var flow = JobFlow.With();
        flow.Agents.UnofferedOn.Add((Personal.Value, "large"));
        var job = await flow.RunningAsync(JobFlow.Request() with { Model = new ModelChoice("large", Option<string>.None) });
        Outcomes.Succeeds(await flow.Jobs.HoldAsync(job.Id, HoldReason.LimitNearlyReached, Cancellation));

        var continued = Outcomes.Succeeds(await flow.Jobs.HandOffAsync(job.Id, new JobHandoff(ToPersonal, Brief), Cancellation));

        Assert.Equal(
            [new ModelChoice("large", Option<string>.None), ModelChoice.Default],
            flow.Agents.Requests.Where(request => request.Connection == Option<ConnectionName>.Some(Personal)).Select(request => request.Model));
        var handedOff = flow.Bus.Published.OfType<JobHandedOff>().Single();
        Assert.Equal((true, new ModelChoice("large", Option<string>.None), continued.Session), (handedOff.ModelFellBack, handedOff.Wanted, handedOff.Session));
        Assert.Equal(new ModelChoice("large", Option<string>.None), job.Model);
    }

    [Fact]
    public async Task OnlyAHeldJobIsHandedOffAndOnlyToAnotherConnectionThatOpensAsync()
    {
        var flow = JobFlow.With();
        var running = await flow.RunningAsync();
        var held = await flow.HeldAsync(HoldReason.LimitNearlyReached);
        flow.Agents.UnknownConnections.Add("nowhere");

        Assert.Equal(JobRejection.NotHeld, Outcomes.FailsWith(await flow.Jobs.HandOffAsync(running.Id, new JobHandoff(ToPersonal, Brief), Cancellation)));
        Assert.Equal(JobRejection.UnknownJob, Outcomes.FailsWith(await flow.Jobs.HandOffAsync(JobId.New(), new JobHandoff(ToPersonal, Brief), Cancellation)));
        Assert.Equal(JobRejection.SameConnection, Outcomes.FailsWith(await flow.Jobs.HandOffAsync(held.Id, new JobHandoff(Choice(FakeAgents.DefaultConnection), Brief), Cancellation)));
        Assert.Equal(JobRejection.EmptyMessage, Outcomes.FailsWith(await flow.Jobs.HandOffAsync(held.Id, new JobHandoff(Choice(Personal), " "), Cancellation)));
        Assert.Equal(JobRejection.UnknownConnection, Outcomes.FailsWith(await flow.Jobs.HandOffAsync(held.Id, new JobHandoff(Choice(new ConnectionName("nowhere")), Brief), Cancellation)));
        Assert.Equal((JobState.Running, JobState.NeedsHelp), (running.State, held.State));
        Assert.Empty(flow.Bus.Published.OfType<JobHandedOff>());
    }

    [Fact]
    public async Task AChoiceSubmittedWithItsConnectionIsKeptWithTheJobAsync()
    {
        var flow = JobFlow.With();

        var job = await flow.SubmittedAsync(JobFlow.Request() with { Connection = Personal, Choice = ToPersonal });
        var other = await flow.SubmittedAsync(JobFlow.Request() with { Connection = Personal, Choice = Choice(new ConnectionName("work")) });

        Assert.Equal(ToPersonal, flow.Store.Choices[job.Id]);
        Assert.False(flow.Store.Choices.ContainsKey(other.Id));
    }

    private static JobFlow Routed(ScriptedRouter router)
    {
        var flow = JobFlow.With(new ScriptedGate(GateVerdict.Retry("Two tests fail")));
        flow.Routers.Add(router);

        return flow;
    }

    private static ConnectionChoice Choice(ConnectionName connection) =>
        new(
            connection,
            ChoiceReason.MostCapacity,
            [
                new CandidateCapacity(FakeAgents.DefaultConnection, 0.95, new UsageLimit("5h", 0.95, Option<DateTimeOffset>.None), 0.9, false),
                new CandidateCapacity(connection, 0.1, Option<UsageLimit>.None, 0.9, true),
            ],
            DateTimeOffset.UnixEpoch);

    private sealed class ScriptedRouter(RoundRoute route) : IRoundRouter
    {
        private readonly ConcurrentQueue<RoundQuestion> asked = new();

        public IReadOnlyList<RoundQuestion> Asked => [.. asked];

        public ValueTask<RoundRoute> RouteAsync(RoundQuestion question, CancellationToken cancellationToken)
        {
            asked.Enqueue(question);

            return ValueTask.FromResult(route);
        }
    }
}
