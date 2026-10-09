using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator.FileSystem;
using Avala.Simulator.Playback;
using Avala.Simulator.Recordings;
using Avala.Simulator.Workloads;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Simulator;

public sealed class SimulatorPlugin(TimeSpan pace, bool developer) : IPlugin
{
    private static readonly TimeSpan DemoPace = TimeSpan.FromMilliseconds(60);

    public SimulatorPlugin()
        : this(DemoPace, DeveloperMode.IsOn)
    {
    }

    public SimulatorPlugin(TimeSpan pace)
        : this(pace, developer: true)
    {
    }

    public PluginInfo Info { get; } = new("avala.simulator", "Simulator");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<IFileWriter, DiskFileWriter>()
            .AddSingleton(provider => new Pacing(provider.GetRequiredService<TimeProvider>(), pace))
            .AddSingleton<IRecordedScenarios, RecordingFolder>()
            .AddSingleton<IWorkloads, DotnetWorkloads>()
            .AddSingleton<ScenarioLibrary>()
            .AddSingleton<Stagecraft>()
            .AddSingleton<IAgentProvider>(provider => new SimulatedProvider(provider.GetRequiredService<Stagecraft>(), SimulatedProvider.Second))
            .AddSingleton<IAgentProvider>(provider => new SimulatedProvider(provider.GetRequiredService<Stagecraft>(), SimulatedProvider.First(developer)))
            .AddSingleton<IConnectionDiscovery>(provider => new LoginDiscovery(provider.GetRequiredService<AvalaPaths>(), developer));
    }
}
