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
            .AddSingleton<IInterventionStore>(services => services.GetRequiredService<SqliteInterventionStore>())
            .AddSingleton<IStartupTask>(services => services.GetRequiredService<SqliteInterventionStore>())
            .AddSingleton<SupervisionBook>()
            .AddSingleton<ISupervision>(services => services.GetRequiredService<SupervisionBook>())
            .AddSingleton<IStartupTask>(services => services.GetRequiredService<SupervisionBook>())
            .AddSingleton<SilenceAlarms>()
            .AddSingleton<Intervener>()
            .AddSingleton<Watchdog>()
            .AddSingleton<IHandle<JobProgressed>>(services => services.GetRequiredService<Watchdog>())
            .AddSingleton<IHandle<JobSessionStarted>>(services => services.GetRequiredService<Watchdog>())
            .AddSingleton<IHandle<AgentActivity>>(services => services.GetRequiredService<Watchdog>())
            .AddSingleton<IHandle<SilenceNoticed>>(services => services.GetRequiredService<Watchdog>());
    }
}
