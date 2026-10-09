using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Sdk;

namespace Avala.Agents.Contracts.Sessions;

public interface IAgentProvider
{
    ProviderInfo Info { get; }

    CapabilitySet CapabilitiesOn(ConnectionEnvironment connection);

    ValueTask<Result<IAgentSession, AgentError>> StartAsync(SessionOptions options, CancellationToken cancellationToken);
}
