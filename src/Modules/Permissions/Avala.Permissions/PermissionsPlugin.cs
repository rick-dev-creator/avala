using Avala.Agents.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Answering;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Permissions.Links;
using Avala.Permissions.PolicyFiles;
using Avala.Permissions.Storage;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Permissions;

public sealed class PermissionsPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.permissions", "Permissions");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<SqliteGovernanceStore>()
            .AddForwarded<IGovernanceStore, SqliteGovernanceStore>()
            .AddForwarded<IStartupTask, SqliteGovernanceStore>()
            .AddSingleton<GovernanceBook>()
            .AddForwarded<IPermissionAudit, GovernanceBook>()
            .AddForwarded<IStartupTask, GovernanceBook>()
            .AddSingleton<PolicyFileReader>()
            .AddSingleton<IRuleFileFormat, PolicyFileFormat>()
            .AddForwarded<IPolicyFiles, PolicyFileReader>()
            .AddForwarded<IRepositoryPolicies, PolicyFileReader>()
            .AddSingleton<IRealPaths, SymbolicLinks>()
            .AddSingleton<PermissionResponder>()
            .AddSingleton<IPermissionAnswers, HumanAnswers>()
            .AddSingleton<SessionGovernor>()
            .AddForwarded<IHandle<SessionOpened>, SessionGovernor>()
            .AddForwarded<IHandle<JobSessionStarted>, SessionGovernor>()
            .AddForwarded<IHandle<AgentActivity>, SessionGovernor>();
    }
}
