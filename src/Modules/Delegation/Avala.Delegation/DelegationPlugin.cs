using Avala.Agents.Contracts;
using Avala.Delegation.Contracts;
using Avala.Delegation.Delegating;
using Avala.Delegation.Escalating;
using Avala.Delegation.MachineFiles;
using Avala.Delegation.Records;
using Avala.Delegation.Reporting;
using Avala.Delegation.RepositoryFiles;
using Avala.Delegation.Resuming;
using Avala.Delegation.Storage;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
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
            .AddSingleton(AnswerChildTool.Definition)
            .AddSingleton<IMachineDelegation, MachineDelegation>()
            .AddSingleton<IDelegationRules, DelegationRulesReader>()
            .AddSingleton<SqliteDelegationStore>()
            .AddForwarded<IDelegationStore, SqliteDelegationStore>()
            .AddForwarded<IStartupTask, SqliteDelegationStore>()
            .AddSingleton<DelegationBook>()
            .AddForwarded<IDelegations, DelegationBook>()
            .AddForwarded<IStartupTask, DelegationBook>()
            .AddSingleton<DelegationJournal>()
            .AddSingleton<ConnectionRouter>()
            .AddSingleton<DelegationPolicy>()
            .AddSingleton<Delegator>()
            .AddSingleton<ChildSpending>()
            .AddSingleton<ChildEvidence>()
            .AddSingleton<ChildReporter>()
            .AddSingleton<DelegationDesk>()
            .AddForwarded<IHandle<SessionOpened>, DelegationDesk>()
            .AddForwarded<IHandle<JobSessionStarted>, DelegationDesk>()
            .AddForwarded<IHandle<AgentActivity>, DelegationDesk>()
            .AddForwarded<IHandle<JobProgressed>, DelegationDesk>()
            .AddForwarded<IHandle<JobHeld>, DelegationDesk>()
            .AddForwarded<IHandle<StartupCompleted>, DelegationDesk>()
            .AddSingleton<ParentResumption>()
            .AddSingleton<DeferredParents>()
            .AddForwarded<IRecoveryDeferral, DeferredParents>()
            .AddForwarded<IHandle<ChildReported>, ParentResumption>()
            .AddForwarded<IHandle<StartupCompleted>, ParentResumption>()
            .AddSingleton<IJobBriefing, OwedReports>()
            .AddSingleton<ChildTerms>()
            .AddForwarded<IJobTerms, ChildTerms>()
            .AddSingleton<ParentNotes>()
            .AddForwarded<IHandle<PermissionDecided>, ParentNotes>()
            .AddForwarded<IHandle<FormDecided>, ParentNotes>()
            .AddSingleton<ChildAnswers>()
            .AddForwarded<IHandle<AgentActivity>, ChildAnswers>();
    }
}
