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
            .AddSingleton<IUsageStore, SqliteUsageStore>()
            .AddSingleton<UsageBook>()
            .AddSingleton<IUsage>(services => services.GetRequiredService<UsageBook>())
            .AddSingleton<IUsageHistory, UsageHistory>()
            .AddSingleton<IStartupTask>(services => services.GetRequiredService<UsageBook>())
            .AddSingleton<IUsageMetrics, UsageMeter>()
            .AddSingleton<UsageTracker>()
            .AddSingleton<IHandle<SessionOpened>>(services => services.GetRequiredService<UsageTracker>())
            .AddSingleton<IHandle<JobSessionStarted>>(services => services.GetRequiredService<UsageTracker>())
            .AddSingleton<IHandle<AgentActivity>>(services => services.GetRequiredService<UsageTracker>());
    }
}
