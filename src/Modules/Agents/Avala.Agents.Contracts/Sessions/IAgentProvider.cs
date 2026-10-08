using Avala.Sdk;

namespace Avala.Agents.Contracts.Sessions;

public interface IAgentProvider
{
    ProviderInfo Info { get; }

    AgentCapabilities Capabilities { get; }

    ValueTask<Result<IAgentSession, AgentError>> StartAsync(SessionOptions options, CancellationToken cancellationToken);
}
