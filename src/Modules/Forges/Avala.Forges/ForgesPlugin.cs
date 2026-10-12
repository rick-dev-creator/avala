using Avala.Forges.Accounts;
using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Delivering;
using Avala.Forges.Detection;
using Avala.Forges.Git;
using Avala.Forges.Polling;
using Avala.Forges.Publishing;
using Avala.Forges.RuleFiles;
using Avala.Forges.Storage;
using Avala.Forges.Transport;
using Avala.Forges.Watching;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Triggers.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Forges;

public sealed class ForgesPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.forges", "Forges");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<SqliteWatchStore>()
            .AddForwarded<IWatchStore, SqliteWatchStore>()
            .AddForwarded<IEventStore, SqliteWatchStore>()
            .AddForwarded<IStartupTask, SqliteWatchStore>()
            .AddSingleton<WatchBook>()
            .AddForwarded<IStartupTask, WatchBook>()
            .AddSingleton<ForgeFile>()
            .AddForwarded<IForgeFile, ForgeFile>()
            .AddForwarded<IForgeFileWriter, ForgeFile>()
            .AddSingleton<ForgeHttp>()
            .AddSingleton<IForgeTransports, ForgeTransports>()
            .AddSingleton<ForgeConnections>()
            .AddForwarded<IForgeCatalog, ForgeConnections>()
            .AddSingleton<IPullRequestRules, PullRequestRulesReader>()
            .AddSingleton<IGitRemote, GitRemote>()
            .AddSingleton<JobPlaces>()
            .AddSingleton<PullRequestReader>()
            .AddSingleton<Waker>()
            .AddSingleton<WatchAdoption>()
            .AddSingleton<PullRequestWatcher>()
            .AddForwarded<IHandle<JobProgressed>, PullRequestWatcher>()
            .AddForwarded<IHandle<PullRequestOpened>, PullRequestWatcher>()
            .AddForwarded<IStartupTask, PullRequestWatcher>()
            .AddSingleton<PullRequestOffers>()
            .AddSingleton<IPullRequests, PullRequestDesk>()
            .AddSingleton<IApprovalStrategy, PullRequestStrategy>()
            .AddSingleton<EventBook>()
            .AddForwarded<IStartupTask, EventBook>()
            .AddSingleton<EventSource>()
            .AddSingleton<EventDispatch>()
            .AddSingleton<ForgeEventPoller>()
            .AddForwarded<IHandle<StartupCompleted>, ForgeEventPoller>()
            .AddForwarded<IHandle<TriggersChanged>, ForgeEventPoller>()
            .AddForwarded<IHandle<ForgeTriggerNudged>, ForgeEventPoller>()
            .AddSingleton<IForgeEventLog, EventLogDesk>()
            .AddSingleton<IForgeNudges, ForgeNudges>()
            .AddSingleton<IRepositoryPublishing, RepositoryPublisher>()
            .AddSingleton<IForgeConnections, ConnectionDesk>()
            .AddSingleton<IForgeProbes, HttpForgeProbes>()
            .AddSingleton<ILoginDetection, LoginDetection>()
            .AddSingleton<CloneUrls>()
            .AddSingleton<ISourceControlAccounts, SourceControlDesk>();
    }
}
