using Avala.Sdk;

namespace Avala.Agents;

public sealed class AgentsPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.agents", "Agents");

    public void Register(IPluginRegistrar registrar)
    {
    }
}
