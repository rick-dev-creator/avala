using Avala.Agents.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Permissions.PolicyFiles;
using Avala.Permissions.Storage;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Permissions.Tests;

public sealed class PermissionsPluginTests
{
    [Fact]
    public async Task TheRegistrationResolvesEveryServiceItRegistersAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Equal(composition.Registered, composition.ResolveEveryRegistration());
    }

    [Fact]
    public async Task EachServiceOfferedUnderSeveralContractsIsOneInstanceAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Same(composition.Get<GovernanceBook>(), composition.Get<IPermissionAudit>());
        Assert.Contains(composition.Get<GovernanceBook>(), composition.All<IStartupTask>());
        Assert.Same(composition.Get<SqliteGovernanceStore>(), composition.Get<IGovernanceStore>());
        Assert.Contains(composition.Get<SqliteGovernanceStore>(), composition.All<IStartupTask>());
        Assert.Same(composition.Get<PolicyFileReader>(), composition.Get<IPolicyFiles>());
        Assert.Same(composition.Get<PolicyFileReader>(), composition.Get<IRepositoryPolicies>());
        Assert.Same(composition.Get<SessionGovernor>(), composition.Get<IHandle<SessionOpened>>());
        Assert.Same(composition.Get<SessionGovernor>(), composition.Get<IHandle<JobSessionStarted>>());
        Assert.Same(composition.Get<SessionGovernor>(), composition.Get<IHandle<AgentActivity>>());
    }

    private static PluginComposition Compose(TemporaryFolder data) =>
        PluginComposition.Of(new PermissionsPlugin(), new AvalaPaths(data.Path), services => services
            .AddSingleton<IAgents>(new AnsweringAgents())
            .AddSingleton<IBaseFiles>(new CommittedFiles()));
}
