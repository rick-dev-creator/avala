using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Agents.Turns;
using Avala.Testing;

namespace Avala.Agents.Tests.Turns;

internal static class Given
{
    public static SessionId Session { get; } = SessionId.New();

    public static TurnId TurnId { get; } = TurnId.New();

    public static DateTimeOffset Now { get; } = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    public static ItemId Item(string name) => new(name);

    public static Turn Turn(params IAgentEvent[] history)
    {
        var turn = Outcomes.Succeeds(Avala.Agents.Turns.Turn.Begin(new TurnStarted(Session, TurnId)));

        foreach (var agentEvent in history)
        {
            Outcomes.Succeeds(turn.Apply(agentEvent, Now));
        }

        return turn;
    }

    public static ItemStarted Started(string item) => new(Session, TurnId, Item(item), ItemKind.Command, item);

    public static ItemProgressed Progressed(string item) => new(Session, TurnId, Item(item), "output");

    public static ItemCompleted Completed(string item) => new(Session, TurnId, Item(item), ItemOutcome.Succeeded);

    public static PermissionRequested PermissionFor(string item) => new(Session, TurnId, Item(item), $"Run {item}", ItemKind.Command, item);

    public static PermissionResolved Resolved(string item) => new(Session, TurnId, Item(item), PermissionAnswer.Allow);

    public static RequestWithdrawn Withdrawn(string item) => new(Session, TurnId, Item(item));

    public static AgentForm Form { get; } = new(
        FormPurpose.Question,
        "Choose a database",
        "The service needs storage.",
        [new FormField("database", "Database", "Which database?", FieldKind.SingleChoice, [new FormOption("SQLite", "A file.", Recommended: true)])]);

    public static FormRequested Asked(string item, AgentForm? form = null) => new(Session, TurnId, Item(item), form ?? Form);

    public static FormAnswered Answered(string item) =>
        new(Session, TurnId, Item(item), new FormAnswer(Item(item), [new FieldAnswer("database") { Chosen = ["SQLite"] }]));

    public static ToolCalled Called(string item) => new(Session, TurnId, Item(item), "propose_follow_up", """{ "instruction": "Document it" }""");

    public static ToolReturned Returned(string item, string answered) => new(Session, TurnId, Item(item), new ToolResult(Item(answered), "Accepted."));

    public static TurnCompleted Ended(TurnOutcome outcome = TurnOutcome.Finished) => new(Session, TurnId, outcome);
}
