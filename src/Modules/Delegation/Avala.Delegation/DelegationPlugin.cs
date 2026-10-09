using Avala.Agents.Contracts;
using Avala.Delegation.Contracts;
using Avala.Delegation.Delegating;
using Avala.Delegation.Records;
using Avala.Delegation.Reporting;
using Avala.Delegation.RepositoryFiles;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Delegation;

public sealed class DelegationPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.delegation", "Delegation");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton(DelegationTool.Definition)
            .AddSingleton<IDelegationRules, DelegationRulesReader>()
            .AddSingleton<DelegationBook>()
            .AddSingleton<IDelegations>(services => services.GetRequiredService<DelegationBook>())
            .AddSingleton<DelegationJournal>()
            .AddSingleton<ConnectionGauge>()
            .AddSingleton<DelegationPolicy>()
            .AddSingleton<Delegator>()
            .AddSingleton<ChildSpending>()
            .AddSingleton<ChildEvidence>()
            .AddSingleton<ChildReporter>()
            .AddSingleton<DelegationDesk>()
            .AddSingleton<IHandle<SessionOpened>>(services => services.GetRequiredService<DelegationDesk>())
            .AddSingleton<IHandle<JobSessionStarted>>(services => services.GetRequiredService<DelegationDesk>())
            .AddSingleton<IHandle<AgentActivity>>(services => services.GetRequiredService<DelegationDesk>())
            .AddSingleton<IHandle<JobProgressed>>(services => services.GetRequiredService<DelegationDesk>())
            .AddSingleton<IHandle<JobHeld>>(services => services.GetRequiredService<DelegationDesk>());
    }
}
