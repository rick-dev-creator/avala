using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;

namespace Avala.Agents.Contracts;

public sealed record AgentRequest(string WorkingDirectory)
{
    public Option<ResumeToken> Resume { get; init; }

    public Option<ConnectionName> Connection { get; init; }
}

public sealed record OpenedSession(SessionId Session, bool Resumed, ConnectionName Connection);

public sealed record AgentTurn(SessionId Session, TurnId Turn);

public sealed record SessionOpened(SessionId Session, ProviderInfo Provider, string WorkingDirectory, ConnectionName Connection) : IIntegrationEvent
{
    public Option<AgentAccount> Account { get; init; }

    public Option<ProcessTreeId> ProcessTree { get; init; }

    public CapabilitySet Capabilities { get; init; } = CapabilitySet.None;
}

public sealed record SessionStopped(SessionId Session) : IIntegrationEvent;

public enum SessionEnding
{
    Closed,
    Crashed,
}

public sealed record SessionEnded(SessionId Session, SessionEnding Ending) : IIntegrationEvent;

public sealed record SessionResumable(SessionId Session, ResumeToken Token) : IIntegrationEvent;

public sealed record AgentActivity(IAgentEvent Event) : IIntegrationEvent;

public sealed record TurnFinished(SessionId Session, TurnId Turn, TurnOutcome Outcome) : IIntegrationEvent;
