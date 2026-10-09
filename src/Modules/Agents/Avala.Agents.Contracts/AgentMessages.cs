using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk.Events;

namespace Avala.Agents.Contracts;

public sealed record AgentRequest(string WorkingDirectory);

public sealed record AgentTurn(SessionId Session, TurnId Turn);

public sealed record SessionOpened(SessionId Session, ProviderInfo Provider, string WorkingDirectory) : IIntegrationEvent;

public sealed record AgentActivity(IAgentEvent Event) : IIntegrationEvent;

public sealed record TurnFinished(SessionId Session, TurnId Turn, TurnOutcome Outcome) : IIntegrationEvent;
