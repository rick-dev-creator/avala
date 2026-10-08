using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Agents.Contracts;

public interface IAgents
{
    ValueTask<Result<SessionId, AgentError>> OpenAsync(AgentRequest request, CancellationToken cancellationToken);

    ValueTask<Result<AgentTurn, AgentError>> SendAsync(SessionId session, string message, CancellationToken cancellationToken);

    ValueTask<Result<SessionId, AgentError>> StopAsync(SessionId session, CancellationToken cancellationToken);
}
