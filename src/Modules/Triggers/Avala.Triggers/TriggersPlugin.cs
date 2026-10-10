using Avala.Autopilot.Contracts;
using Avala.Sdk;
using Avala.Triggers.Catalog;
using Avala.Triggers.Contracts;
using Avala.Triggers.Firing;
using Avala.Triggers.Listening;
using Avala.Triggers.Looping;
using Avala.Triggers.Receiving;
using Avala.Triggers.Records;
using Avala.Triggers.Scheduling;
using Avala.Triggers.Storage;
using Avala.Triggers.TriggerFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Triggers;

public sealed class TriggersPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.triggers", "Triggers");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<SqliteTriggerStore>()
            .AddForwarded<IScheduleStore, SqliteTriggerStore>()
            .AddForwarded<IRunStore, SqliteTriggerStore>()
            .AddForwarded<IStartupTask, SqliteTriggerStore>()
            .AddSingleton<ScheduleBook>()
            .AddForwarded<IStartupTask, ScheduleBook>()
            .AddSingleton<RunJournal>()
            .AddForwarded<IStartupTask, RunJournal>()
            .AddSingleton<ISecrets, EnvironmentSecrets>()
            .AddSingleton<WebhookGate>()
            .AddForwarded<IStartupTask, WebhookGate>()
            .AddSingleton<ITriggerFiles, TriggerFileReader>()
            .AddSingleton<TriggerCatalog>()
            .AddSingleton<LoopQueue>()
            .AddSingleton<IJobSource, TriggerTaskSource>()
            .AddSingleton<FiringChecks>()
            .AddSingleton<TriggerDispatch>()
            .AddSingleton<TriggerFirer>()
            .AddSingleton<TriggerScheduler>()
            .AddForwarded<IStartupTask, TriggerScheduler>()
            .AddSingleton<WebhookDesk>()
            .AddSingleton<WebhookListener>()
            .AddForwarded<IWebhookEndpoint, WebhookListener>()
            .AddForwarded<IStartupTask, WebhookListener>()
            .AddSingleton<TriggerBook>()
            .AddForwarded<ITriggers, TriggerBook>();
    }
}
