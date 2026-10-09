using Avala.Agents.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Supervision.Contracts;
using Avala.Supervision.Settings;
using Avala.Supervision.Storage;
using Avala.Supervision.Supervising;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Supervision;

public sealed class SupervisionPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.supervision", "Supervision");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<ISupervisionSettings, SettingsFile>()
            .AddSingleton<SqliteInterventionStore>()
            .AddForwarded<IInterventionStore, SqliteInterventionStore>()
            .AddForwarded<IStartupTask, SqliteInterventionStore>()
            .AddSingleton<SupervisionBook>()
            .AddForwarded<ISupervision, SupervisionBook>()
            .AddForwarded<IStartupTask, SupervisionBook>()
            .AddSingleton<SilenceAlarms>()
            .AddSingleton<Intervener>()
            .AddSingleton<Watchdog>()
            .AddForwarded<IHandle<JobProgressed>, Watchdog>()
            .AddForwarded<IHandle<JobSessionStarted>, Watchdog>()
            .AddForwarded<IHandle<AgentActivity>, Watchdog>()
            .AddForwarded<IHandle<SilenceNoticed>, Watchdog>();
    }
}
