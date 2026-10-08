using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator.Application;
using Avala.Simulator.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Simulator;

public sealed class SimulatorPlugin(TimeSpan pace) : IPlugin
{
    private static readonly TimeSpan DemoPace = TimeSpan.FromMilliseconds(60);

    public SimulatorPlugin()
        : this(DemoPace)
    {
    }

    public PluginInfo Info { get; } = new("avala.simulator", "Simulator");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<IFileWriter, DiskFileWriter>()
            .AddSingleton(provider => new Pacing(provider.GetRequiredService<TimeProvider>(), pace))
            .AddSingleton<IAgentProvider, SimulatedProvider>();
    }
}
