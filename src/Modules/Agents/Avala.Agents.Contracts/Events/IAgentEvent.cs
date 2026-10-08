using Avala.Agents.Contracts.Sessions;

namespace Avala.Agents.Contracts.Events;

public interface IAgentEvent
{
    SessionId Session { get; }

    TurnId Turn { get; }
}
