using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Agents.Turns;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Agents.Tests.Turns;

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
        Assert.Equal(Option<ItemId>.Some(Given.Item("deploy")), turn.PendingPermission);

        Outcomes.Succeeds(turn.Apply(Given.Resolved("deploy"), Given.Now));

        Assert.Equal(TurnState.Working, turn.State);
        Assert.Equal(Option<ItemId>.None, turn.PendingPermission);
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
    public void AFormOpensAnItemThatWaitsForItsAnswerAndThenCompletes()
    {
        var turn = Given.Turn();
        IAgentEvent[] life = [Given.Asked("question"), Given.Answered("question"), Given.Completed("question")];

        var forwarded = life.Select(agentEvent =>
        {
            var events = Outcomes.Succeeds(turn.Apply(agentEvent, Given.Now)).Events;

            return (events, turn.State, turn.PendingForm);
        }).ToList();

        Assert.Equal(life.Select(agentEvent => new[] { agentEvent }), forwarded.Select(step => step.events));
        Assert.Equal(
            [
                (TurnState.AwaitingAnswer, Option<ItemId>.Some(Given.Item("question"))),
                (TurnState.Working, Option<ItemId>.None),
                (TurnState.Working, Option<ItemId>.None),
            ],
            forwarded.Select(step => (step.State, step.PendingForm)));
        Assert.Empty(turn.OpenItems);
    }

    public static TheoryData<string, AgentForm> MalformedForms => new()
    {
        { "no field", Given.Form with { Fields = [] } },
        { "undefined purpose", Given.Form with { Purpose = (FormPurpose)42 } },
        { "a choice without options", Given.Form with { Fields = [Field(FieldKind.SingleChoice)] } },
        { "free text with options", Given.Form with { Fields = [Field(FieldKind.FreeText, new FormOption("a", ""))] } },
        { "two recommended options", Given.Form with { Fields = [Field(FieldKind.MultipleChoice, new FormOption("a", "", true), new FormOption("b", "", true))] } },
        { "an option without a label", Given.Form with { Fields = [Field(FieldKind.SingleChoice, new FormOption(" ", ""))] } },
        { "two options with one label", Given.Form with { Fields = [Field(FieldKind.SingleChoice, new FormOption("a", ""), new FormOption("a", ""))] } },
        { "two fields with one id", Given.Form with { Fields = [Field(FieldKind.FreeText), Field(FieldKind.Confirmation)] } },
        { "a field without an id", Given.Form with { Fields = [Field(FieldKind.FreeText) with { Id = "" }] } },
    };

    [Theory]
    [MemberData(nameof(MalformedForms))]
    public void RejectsAMalformedFormWithoutOpeningItsItem(string malformation, AgentForm form)
    {
        var turn = Given.Turn();

        Assert.Equal(TurnError.MalformedForm, Outcomes.FailsWith(turn.Apply(Given.Asked("question", form), Given.Now)));
        Assert.Equal((TurnState.Working, 0), (turn.State, turn.OpenItems.Count));
        Assert.NotEmpty(malformation);
    }

    [Fact]
    public void RejectsFormsAndAnswersThatDoNotFitTheTurn()
    {
        var turn = Given.Turn(Given.Started("deploy"), Given.Asked("question"));

        Assert.Equal(TurnError.FormAlreadyPending, Outcomes.FailsWith(turn.Apply(Given.Asked("another"), Given.Now)));
        Assert.Equal(TurnError.ItemAlreadyStarted, Outcomes.FailsWith(turn.Apply(Given.Asked("deploy"), Given.Now)));
        Assert.Equal(TurnError.PermissionAlreadyPending, Outcomes.FailsWith(turn.Apply(Given.PermissionFor("deploy"), Given.Now)));
        Assert.Equal(TurnError.NoPendingForm, Outcomes.FailsWith(turn.Apply(Given.Answered("deploy"), Given.Now)));
        Assert.Equal(TurnState.AwaitingAnswer, turn.State);
    }

    [Fact]
    public void AWithdrawnPermissionOrFormStopsWaitingAndCanNoLongerBeAnswered()
    {
        var turn = Given.Turn(Given.Started("deploy"), Given.PermissionFor("deploy"));

        Outcomes.Succeeds(turn.Apply(Given.Withdrawn("deploy"), Given.Now));
        var afterPermission = (turn.State, turn.PendingPermission, Outcomes.FailsWith(turn.Apply(Given.Resolved("deploy"), Given.Now)));
        Outcomes.Succeeds(turn.Apply(Given.Asked("question"), Given.Now));
        Outcomes.Succeeds(turn.Apply(Given.Withdrawn("question"), Given.Now));

        Assert.Equal((TurnState.Working, Option<ItemId>.None, TurnError.NoPendingPermission), afterPermission);
        Assert.Equal((TurnState.Working, Option<ItemId>.None), (turn.State, turn.PendingForm));
        Assert.Equal(TurnError.NoPendingForm, Outcomes.FailsWith(turn.Apply(Given.Answered("question"), Given.Now)));
    }

    [Fact]
    public void RejectsWithdrawingARequestThatIsNotPending()
    {
        var turn = Given.Turn(Given.Started("deploy"), Given.Started("migrate"), Given.PermissionFor("deploy"));

        Assert.Equal(TurnError.NoPendingRequest, Outcomes.FailsWith(turn.Apply(Given.Withdrawn("migrate"), Given.Now)));
        Assert.Equal(TurnState.AwaitingPermission, turn.State);
    }

    [Fact]
    public void AToolCallOpensAnItemThatWaitsForItsResultAndThenCompletes()
    {
        var turn = Given.Turn();
        IAgentEvent[] life = [Given.Called("propose"), Given.Returned("propose", "propose"), Given.Completed("propose")];

        var forwarded = life.Select(agentEvent => (Outcomes.Succeeds(turn.Apply(agentEvent, Given.Now)).Events, turn.PendingCalls.ToList())).ToList();

        Assert.Equal(life.Select(agentEvent => new[] { agentEvent }), forwarded.Select(step => step.Events));
        Assert.Equal([[Given.Item("propose")], [], []], forwarded.Select(step => step.Item2));
        Assert.Equal((TurnState.Working, 0), (turn.State, turn.OpenItems.Count));
    }

    [Fact]
    public void RejectsResultsThatAnswerNoPendingCall()
    {
        var turn = Given.Turn(Given.Started("build"), Given.Called("propose"));

        Assert.Equal(TurnError.NoPendingCall, Outcomes.FailsWith(turn.Apply(Given.Returned("build", "build"), Given.Now)));
        Assert.Equal(TurnError.NoPendingCall, Outcomes.FailsWith(turn.Apply(Given.Returned("propose", "other"), Given.Now)));
        Assert.Equal(TurnError.ItemAlreadyStarted, Outcomes.FailsWith(turn.Apply(Given.Called("build"), Given.Now)));
        Assert.Equal([Given.Item("propose")], turn.PendingCalls);
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

    private static FormField Field(FieldKind kind, params FormOption[] options) => new("field", "Field", "Prompt?", kind, options);
}
