using Avala.ForgeSimulator.Remote;
using Avala.Forges.Contracts;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.ForgeSimulator;

public sealed class ForgeSimulatorPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.forge-simulator", "Simulated forge");

    public void Register(IPluginRegistrar registrar) => registrar.Services.AddSingleton<IForge, SimulatedForge>();
}
