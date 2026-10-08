using Avala.Agents.Contracts.Events;
using Avala.Sdk.Domain;

namespace Avala.Agents.Turns;

internal sealed record TurnProgress(IReadOnlyList<IAgentEvent> Events) : IDomainEvent;
