using Avala.Agents.Contracts;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Resources.Claims;
using Avala.Resources.Contracts;
using Avala.Resources.Disks;
using Avala.Resources.History;
using Avala.Resources.Housekeeping;
using Avala.Resources.Leasing;
using Avala.Resources.Reaping;
using Avala.Resources.Sampling;
using Avala.Resources.Scratch;
using Avala.Resources.Settings;
using Avala.Resources.Storage;
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
            .AddSingleton<TemporaryFolders>()
            .AddForwarded<IProcessEnvironment, TemporaryFolders>()
            .AddForwarded<IHandle<JobProgressed>, TemporaryFolders>()
            .AddSingleton<IPortClaims>(services => new PortClaimFiles(services.GetRequiredService<IListeningPorts>(), PortClaimFiles.MachineFolder))
            .AddSingleton<PortLeases>()
            .AddForwarded<IProcessEnvironment, PortLeases>()
            .AddForwarded<IPortLeases, PortLeases>()
            .AddSingleton<ResourceBook>()
            .AddForwarded<IResources, ResourceBook>()
            .AddSingleton<ProcessReadings>()
            .AddSingleton<OrphanReaper>()
            .AddForwarded<IOrphans, OrphanReaper>()
            .AddSingleton<RetainedWorktrees>()
            .AddSingleton<IWorktreeFacts, WorktreeFacts>()
            .AddSingleton<WorktreeHousekeeper>()
            .AddSingleton<IWorktreeHousekeeping, WorktreeDesk>()
            .AddForwarded<IStartupTask, WorktreeHousekeeper>()
            .AddSingleton<SqliteWorktreeHistoryStore>()
            .AddForwarded<IWorktreeHistoryStore, SqliteWorktreeHistoryStore>()
            .AddForwarded<IStartupTask, SqliteWorktreeHistoryStore>()
            .AddSingleton<WorktreeHistory>()
            .AddForwarded<IWorktreeHistory, WorktreeHistory>()
            .AddForwarded<IHandle<ResourcesSampled>, WorktreeHistory>()
            .AddForwarded<IHandle<WorktreeFreed>, WorktreeHistory>()
            .AddForwarded<IHandle<WorktreeReclaimed>, WorktreeHistory>()
            .AddSingleton<WorktreeMoments>()
            .AddForwarded<IHandle<ConversationsArchived>, WorktreeMoments>()
            .AddForwarded<IHandle<PullRequestWatchChanged>, WorktreeMoments>()
            .AddSingleton<RetentionRecovery>()
            .AddForwarded<IStartupTask, RetentionRecovery>()
            .AddSingleton<SampleTaker>()
            .AddSingleton<ResourceSampler>()
            .AddForwarded<IStartupTask, ResourceSampler>()
            .AddSingleton<ResourceTracker>()
            .AddForwarded<IHandle<SessionOpened>, ResourceTracker>()
            .AddForwarded<IHandle<JobSessionStarted>, ResourceTracker>()
            .AddForwarded<IHandle<JobSessionRested>, ResourceTracker>()
            .AddForwarded<IHandle<SessionEnded>, ResourceTracker>()
            .AddForwarded<IHandle<SessionStopped>, ResourceTracker>()
            .AddForwarded<IHandle<JobProgressed>, ResourceTracker>()
            .AddForwarded<IHandle<ResourcesSampled>, ResourceTracker>();
    }
}
