using Avala.Agents.Contracts.Events;
using Avala.Sdk.Domain;

namespace Avala.Agents.Domain;

internal sealed record TurnProgress(IReadOnlyList<IAgentEvent> Events) : IDomainEvent;
