using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Agents.Domain;
using Avala.Testing;

namespace Avala.Agents.Tests.Domain;

public sealed class TurnTests
{
    [Fact]
    public void AcceptsAnItemThroughItsWholeLife()
    {
        var turn = Given.Turn();
        IAgentEvent[] life = [Given.Started("build"), Given.Progressed("build"), Given.Completed("build")];

        var forwarded = life.Select(agentEvent => Outcomes.Succeeds(turn.Apply(agentEvent, Given.Now)).Events);

        Assert.Equal(life.Select(agentEvent => new[] { agentEvent }), forwarded);
        Assert.Empty(turn.OpenItems);
    }

    [Fact]
    public void AcceptsACanvasStreamedInChunks()
    {
        var turn = Given.Turn();
        IAgentEvent[] canvas =
        [
            new CanvasStarted(Given.Session, Given.TurnId, Given.Item("chart"), "Coverage by module", "image/svg+xml"),
            new ItemProgressed(Given.Session, Given.TurnId, Given.Item("chart"), "<svg viewBox=\"0 0 400 200\">"),
            new ItemProgressed(Given.Session, Given.TurnId, Given.Item("chart"), "<rect width=\"320\" height=\"24\"/></svg>"),
            new ItemCompleted(Given.Session, Given.TurnId, Given.Item("chart"), ItemOutcome.Succeeded),
        ];

        var forwarded = canvas.Select(agentEvent => Outcomes.Succeeds(turn.Apply(agentEvent, Given.Now)).Events);

        Assert.Equal(canvas.Select(agentEvent => new[] { agentEvent }), forwarded);
        Assert.Empty(turn.OpenItems);
    }

    [Fact]
    public void RejectsActivityOnAnItemThatNeverStarted()
    {
        var turn = Given.Turn();

        Assert.Equal(TurnError.UnknownItem, Outcomes.FailsWith(turn.Apply(Given.Progressed("build"), Given.Now)));
        Assert.Equal(TurnError.UnknownItem, Outcomes.FailsWith(turn.Apply(Given.Completed("build"), Given.Now)));
    }

    [Fact]
    public void RejectsStartingAnItemTwice()
    {
        var turn = Given.Turn(Given.Started("build"));

        Assert.Equal(TurnError.ItemAlreadyStarted, Outcomes.FailsWith(turn.Apply(Given.Started("build"), Given.Now)));
    }

    [Fact]
    public void RejectsActivityOnACompletedItem()
    {
        var turn = Given.Turn(Given.Started("build"), Given.Completed("build"));

        Assert.Equal(TurnError.ItemAlreadyCompleted, Outcomes.FailsWith(turn.Apply(Given.Progressed("build"), Given.Now)));
        Assert.Equal(TurnError.ItemAlreadyCompleted, Outcomes.FailsWith(turn.Apply(Given.Completed("build"), Given.Now)));
        Assert.Equal(TurnError.ItemAlreadyStarted, Outcomes.FailsWith(turn.Apply(Given.Started("build"), Given.Now)));
    }

    [Fact]
    public void RejectsEventsFromAnotherSessionOrTurn()
    {
        var turn = Given.Turn();

        Assert.Equal(TurnError.ForeignEvent, Outcomes.FailsWith(turn.Apply(Given.Started("build") with { Session = SessionId.New() }, Given.Now)));
        Assert.Equal(TurnError.ForeignEvent, Outcomes.FailsWith(turn.Apply(Given.Started("build") with { Turn = TurnId.New() }, Given.Now)));
    }

    [Fact]
    public void RejectsASecondTurnStart() =>
        Assert.Equal(TurnError.UnexpectedTurnStart, Outcomes.FailsWith(Given.Turn().Apply(new TurnStarted(Given.Session, Given.TurnId), Given.Now)));

    [Fact]
    public void WaitsForPermissionUntilItIsResolved()
    {
        var turn = Given.Turn(Given.Started("deploy"), Given.PermissionFor("deploy"));

        Assert.Equal(TurnState.AwaitingPermission, turn.State);
        Assert.Equal(Given.Item("deploy"), turn.PendingPermission);

        Outcomes.Succeeds(turn.Apply(Given.Resolved("deploy"), Given.Now));

        Assert.Equal(TurnState.Working, turn.State);
        Assert.Null(turn.PendingPermission);
    }

    [Fact]
    public void RejectsPermissionsThatDoNotFitTheTurn()
    {
        var turn = Given.Turn(Given.Started("deploy"), Given.Started("migrate"), Given.PermissionFor("deploy"));

        Assert.Equal(TurnError.UnknownItem, Outcomes.FailsWith(turn.Apply(Given.PermissionFor("publish"), Given.Now)));
        Assert.Equal(TurnError.PermissionAlreadyPending, Outcomes.FailsWith(turn.Apply(Given.PermissionFor("migrate"), Given.Now)));
        Assert.Equal(TurnError.NoPendingPermission, Outcomes.FailsWith(turn.Apply(Given.Resolved("migrate"), Given.Now)));
    }

    [Fact]
    public void EndsInTheStateOfItsOutcome()
    {
        var states = Enum.GetValues<TurnOutcome>().Select(outcome =>
        {
            var turn = Given.Turn(Given.Ended(outcome));

            return turn.State;
        });

        Assert.Equal([TurnState.Finished, TurnState.Interrupted, TurnState.Failed], states);
    }

    [Fact]
    public void ClosesOpenItemsAsAbandonedBeforeTheTurnEnds()
    {
        var turn = Given.Turn(Given.Started("build"), Given.Started("test"));

        var forwarded = Outcomes.Succeeds(turn.Apply(Given.Ended(), Given.Now)).Events;

        IAgentEvent[] expected =
        [
            new ItemCompleted(Given.Session, Given.TurnId, Given.Item("build"), ItemOutcome.Abandoned),
            new ItemCompleted(Given.Session, Given.TurnId, Given.Item("test"), ItemOutcome.Abandoned),
            Given.Ended(),
        ];
        Assert.Equal(expected, forwarded);
    }

    [Fact]
    public void RejectsEverythingAfterTheTurnEnds()
    {
        var turn = Given.Turn(Given.Ended());

        Assert.Equal(TurnError.TurnEnded, Outcomes.FailsWith(turn.Apply(Given.Started("build"), Given.Now)));
        Assert.Equal(TurnError.TurnEnded, Outcomes.FailsWith(turn.Apply(Given.Ended(), Given.Now)));
    }

    [Fact]
    public void ForwardsTelemetryWhileTheTurnIsLive()
    {
        var turn = Given.Turn();
        IAgentEvent[] telemetry =
        [
            new UsageReported(Given.Session, Given.TurnId, new TokenUsage(1200, 340, 800, 0, 90), new Cost(0.02m, "USD")),
            new LimitReported(Given.Session, Given.TurnId, new UsageLimit("5 hours", 0.72, Given.Now.AddHours(2))),
            new PlanUpdated(Given.Session, Given.TurnId, [new PlanStep("Write tests", PlanStepStatus.InProgress)]),
        ];

        var forwarded = telemetry.Select(agentEvent => Outcomes.Succeeds(turn.Apply(agentEvent, Given.Now)).Events);

        Assert.Equal(telemetry.Select(agentEvent => new[] { agentEvent }), forwarded);
    }
}
