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

    public static TurnCompleted Ended(TurnOutcome outcome = TurnOutcome.Finished) => new(Session, TurnId, outcome);
}
