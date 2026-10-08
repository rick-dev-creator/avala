using Avala.Agents.Contracts;
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
        registrar.Services.AddSingleton<IAgents, AgentSessions>();
    }
}
