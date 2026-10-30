using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Sdk;

namespace Avala.Jobs.Tests.Coordination;

public sealed class RelayAdversaryTests
{
    private const string Brief = "This job was handed off to you from default.";

    private static readonly ConnectionName Personal = new("personal");

    [Fact]
    public async Task AJobWithNoRetriesLeftWhoseTurnWasWoundDownStillMovesToTheOtherConnectionAsync()
    {
        var flow = Relayed(RoundRoute.To(new JobHandoff(Choice(Personal), Brief)));
        var job = await flow.RunningAsync(attemptsPerRound: 1);

        await flow.FinishTurnAsync(job);

        Assert.Equal((JobState.Running, Option<ConnectionName>.Some(Personal)), (job.State, job.Connection));
        Assert.Equal(
            [(AttemptOrigin.Initial, AttemptOutcome.Rejected), (AttemptOrigin.Handoff, AttemptOutcome.Running)],
            job.Attempts.Select(attempt => (attempt.Origin, attempt.Outcome)));
    }

    [Fact]
    public async Task AJobWithNoRetriesLeftWhoseTurnWasWoundDownWithNowhereToGoIsHeldAtItsLimitAsync()
    {
        var flow = Relayed(RoundRoute.Held(HoldReason.LimitNearlyReached));
        var job = await flow.RunningAsync(attemptsPerRound: 1);

        await flow.FinishTurnAsync(job);

        Assert.Equal(JobState.NeedsHelp, job.State);
        Assert.Equal(HoldReason.LimitNearlyReached, flow.Bus.Published.OfType<Contracts.JobHeld>().Single().Hold.Reason);
    }

    [Fact]
    public async Task AJobWithNoRetriesLeftWhoseTurnWasWoundDownButNoLongerNeedsToMoveIsHeldAtItsLimitAsync()
    {
        var flow = Relayed(RoundRoute.GoOn);
        var job = await flow.RunningAsync(attemptsPerRound: 1);

        await flow.FinishTurnAsync(job);

        Assert.Equal(JobState.NeedsHelp, job.State);
        Assert.Equal(HoldReason.LimitNearlyReached, flow.Bus.Published.OfType<Contracts.JobHeld>().Single().Hold.Reason);
    }

    [Fact]
    public async Task AJobWithNoRetriesLeftWhoseRelayCannotOpenIsHeldAtItsLimitAsync()
    {
        var flow = Relayed(RoundRoute.To(new JobHandoff(Choice(new ConnectionName("nowhere")), Brief)));
        flow.Agents.UnknownConnections.Add("nowhere");
        var job = await flow.RunningAsync(attemptsPerRound: 1);

        await flow.FinishTurnAsync(job);

        Assert.Equal((JobState.NeedsHelp, Option<ConnectionName>.Some(FakeAgents.DefaultConnection)), (job.State, job.Connection));
        Assert.Equal(HoldReason.LimitNearlyReached, flow.Bus.Published.OfType<Contracts.JobHeld>().Single().Hold.Reason);
    }

    private static JobFlow Relayed(RoundRoute route)
    {
        var flow = JobFlow.With(new ScriptedGate(GateVerdict.Relay("Wound down.")));
        flow.Routers.Add(new FixedRouter(route));

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

    private sealed class FixedRouter(RoundRoute route) : IRoundRouter
    {
        public ValueTask<RoundRoute> RouteAsync(RoundQuestion question, CancellationToken cancellationToken) => ValueTask.FromResult(route);
    }
}
