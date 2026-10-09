using Avala.Agents.Contracts;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Disks;
using Avala.Resources.Housekeeping;
using Avala.Resources.Leasing;
using Avala.Resources.Reaping;
using Avala.Resources.Sampling;
using Avala.Resources.Settings;
using Avala.Resources.Tracking;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Resources;

public sealed class ResourcesPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.resources", "Resources");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<IResourceSettings, ResourceSettingsFile>()
            .AddSingleton<IFolderSizes, FolderSizes>()
            .AddSingleton<PortLeases>()
            .AddSingleton<IProcessEnvironment>(services => services.GetRequiredService<PortLeases>())
            .AddSingleton<ResourceBook>()
            .AddSingleton<IResources>(services => services.GetRequiredService<ResourceBook>())
            .AddSingleton<ProcessReadings>()
            .AddSingleton<OrphanReaper>()
            .AddSingleton<IOrphans>(services => services.GetRequiredService<OrphanReaper>())
            .AddSingleton<WorktreeHousekeeper>()
            .AddSingleton<IWorktreeHousekeeping>(services => services.GetRequiredService<WorktreeHousekeeper>())
            .AddSingleton<IStartupTask>(services => services.GetRequiredService<WorktreeHousekeeper>())
            .AddSingleton<SampleTaker>()
            .AddSingleton<ResourceSampler>()
            .AddSingleton<IStartupTask>(services => services.GetRequiredService<ResourceSampler>())
            .AddSingleton<ResourceTracker>()
            .AddSingleton<IHandle<SessionOpened>>(services => services.GetRequiredService<ResourceTracker>())
            .AddSingleton<IHandle<JobSessionStarted>>(services => services.GetRequiredService<ResourceTracker>())
            .AddSingleton<IHandle<SessionEnded>>(services => services.GetRequiredService<ResourceTracker>())
            .AddSingleton<IHandle<SessionStopped>>(services => services.GetRequiredService<ResourceTracker>())
            .AddSingleton<IHandle<JobProgressed>>(services => services.GetRequiredService<ResourceTracker>())
            .AddSingleton<IHandle<ResourcesSampled>>(services => services.GetRequiredService<ResourceTracker>());
    }
}
