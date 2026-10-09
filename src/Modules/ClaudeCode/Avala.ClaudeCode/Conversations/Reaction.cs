using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;

namespace Avala.ClaudeCode.Conversations;

internal sealed record Stamp(SessionId Session, TurnId Turn);

internal sealed record Reaction(IReadOnlyList<IAgentEvent> Events, IReadOnlyList<JsonNode> Outgoing)
{
    public static Reaction None { get; } = new([], []);

    public static Reaction Of(params IReadOnlyList<IAgentEvent> events) => new(events, []);

    public static Reaction Send(params IReadOnlyList<JsonNode> outgoing) => new([], outgoing);

    public Reaction Then(Reaction next) => new([.. Events, .. next.Events], [.. Outgoing, .. next.Outgoing]);
}
