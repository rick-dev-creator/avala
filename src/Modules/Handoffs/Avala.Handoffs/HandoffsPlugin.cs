using Avala.Agents.Contracts;
using Avala.Handoffs.Briefing;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Records;
using Avala.Handoffs.RuleFiles;
using Avala.Handoffs.Storage;
using Avala.Handoffs.Watching;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Handoffs;

public sealed class HandoffsPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.handoffs", "Handoffs");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<SqliteHandoffStore>()
            .AddForwarded<IHandoffStore, SqliteHandoffStore>()
            .AddForwarded<IStartupTask, SqliteHandoffStore>()
            .AddSingleton<ILimitRules, LimitRulesReader>()
            .AddSingleton<JobSpending>()
            .AddSingleton<HandoffBook>()
            .AddForwarded<IHandoffs, HandoffBook>()
            .AddForwarded<IStartupTask, HandoffBook>()
            .AddForwarded<IHandle<JobHandedOff>, HandoffBook>()
            .AddSingleton<JobNotes>()
            .AddForwarded<IHandle<JobSessionStarted>, JobNotes>()
            .AddForwarded<IHandle<AgentActivity>, JobNotes>()
            .AddSingleton<BriefGatherer>()
            .AddSingleton<JobPlaces>()
            .AddSingleton<ConnectionReadings>()
            .AddSingleton<Situations>()
            .AddSingleton<LimitActions>()
            .AddSingleton<LimitWatch>()
            .AddForwarded<IHandle<JobHeld>, LimitWatch>()
            .AddForwarded<IHandle<JobProgressed>, LimitWatch>()
            .AddForwarded<IStartupTask, LimitWatch>()
            .AddSingleton<IRoundRouter, RoundGuard>();
    }
}
