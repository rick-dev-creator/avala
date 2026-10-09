using Avala.Agents.ConnectionFiles;
using Avala.Agents.Connections;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Credentials;
using Avala.Agents.Sessions;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Agents;

public sealed class AgentsPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.agents", "Agents");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<IConnectionFile, ConnectionFileReader>()
            .AddSingleton<ICredentialSource, LoginFolderSource>()
            .AddSingleton<ICredentialSource, ApiKeySource>()
            .AddSingleton<ConnectionRegistry>()
            .AddForwarded<IConnections, ConnectionRegistry>()
            .AddSingleton<ProviderChain>()
            .AddSingleton<SessionStarter>()
            .AddSingleton<AgentSessions>()
            .AddForwarded<IAgents, AgentSessions>()
            .AddForwarded<IShutdownTask, AgentSessions>();
    }
}
