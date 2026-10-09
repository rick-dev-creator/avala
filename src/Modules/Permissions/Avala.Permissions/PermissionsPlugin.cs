using Avala.Agents.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Answering;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
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
            .AddSingleton<IGovernanceStore>(services => services.GetRequiredService<SqliteGovernanceStore>())
            .AddSingleton<IStartupTask>(services => services.GetRequiredService<SqliteGovernanceStore>())
            .AddSingleton<GovernanceBook>()
            .AddSingleton<IPermissionAudit>(services => services.GetRequiredService<GovernanceBook>())
            .AddSingleton<IStartupTask>(services => services.GetRequiredService<GovernanceBook>())
            .AddSingleton<PolicyFileReader>()
            .AddSingleton<IRuleFileFormat, PolicyFileFormat>()
            .AddSingleton<IPolicyFiles>(services => services.GetRequiredService<PolicyFileReader>())
            .AddSingleton<IRepositoryPolicies>(services => services.GetRequiredService<PolicyFileReader>())
            .AddSingleton<PermissionResponder>()
            .AddSingleton<IPermissionAnswers, HumanAnswers>()
            .AddSingleton<SessionGovernor>()
            .AddSingleton<IHandle<SessionOpened>>(services => services.GetRequiredService<SessionGovernor>())
            .AddSingleton<IHandle<JobSessionStarted>>(services => services.GetRequiredService<SessionGovernor>())
            .AddSingleton<IHandle<AgentActivity>>(services => services.GetRequiredService<SessionGovernor>());
    }
}
