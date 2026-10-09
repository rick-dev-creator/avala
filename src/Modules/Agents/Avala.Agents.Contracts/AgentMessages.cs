using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Agents.Contracts;

public sealed record AgentRequest(string WorkingDirectory)
{
    public Option<ResumeToken> Resume { get; init; }
}

public sealed record OpenedSession(SessionId Session, bool Resumed);

public sealed record AgentTurn(SessionId Session, TurnId Turn);

public sealed record SessionOpened(SessionId Session, ProviderInfo Provider, string WorkingDirectory) : IIntegrationEvent
{
    public Option<AgentAccount> Account { get; init; }
}

public enum SessionEnding
{
    Closed,
    Crashed,
}

public sealed record SessionEnded(SessionId Session, SessionEnding Ending) : IIntegrationEvent;

public sealed record SessionResumable(SessionId Session, ResumeToken Token) : IIntegrationEvent;

public sealed record AgentActivity(IAgentEvent Event) : IIntegrationEvent;

public sealed record TurnFinished(SessionId Session, TurnId Turn, TurnOutcome Outcome) : IIntegrationEvent;
