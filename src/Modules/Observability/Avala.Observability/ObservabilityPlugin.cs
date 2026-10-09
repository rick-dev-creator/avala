using Avala.Agents.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Observability.Metrics;
using Avala.Observability.Storage;
using Avala.Observability.Tracking;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Observability;

public sealed class ObservabilityPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.observability", "Observability");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<SqliteUsageStore>()
            .AddForwarded<IUsageStore, SqliteUsageStore>()
            .AddForwarded<IStartupTask, SqliteUsageStore>()
            .AddSingleton<UsageBook>()
            .AddForwarded<IUsage, UsageBook>()
            .AddForwarded<IUsageSessions, UsageBook>()
            .AddSingleton<IUsageHistory, UsageHistory>()
            .AddForwarded<IStartupTask, UsageBook>()
            .AddSingleton<IUsageMetrics, UsageMeter>()
            .AddSingleton<UsageTracker>()
            .AddForwarded<IHandle<SessionOpened>, UsageTracker>()
            .AddForwarded<IHandle<JobSessionStarted>, UsageTracker>()
            .AddForwarded<IHandle<AgentActivity>, UsageTracker>();
    }
}
