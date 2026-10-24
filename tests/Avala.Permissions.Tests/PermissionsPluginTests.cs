using Avala.Agents.Contracts.Sessions;
using Avala.Agents.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Folders;
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
    public async Task EveryHandlerIsSubscribedToEachEventItHandlesAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Empty(composition.EventsHandledButNotSubscribed());
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
        Assert.Contains(composition.Get<SessionGovernor>(), composition.All<IHandle<AgentActivity>>());
        Assert.Same(composition.Get<SessionGovernor>(), composition.Get<IHandle<JobProgressed>>());
    }

    [Fact]
    public async Task TheFolderToolIsOfferedAndItsCallsAreHandledAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Contains(FolderTool.Definition, composition.All<HarnessTool>());
        Assert.Single(composition.All<IHandle<AgentActivity>>().OfType<FolderDesk>());
    }

    private static PluginComposition Compose(TemporaryFolder data) =>
        PluginComposition.Of(new PermissionsPlugin(), new AvalaPaths(data.Path), services => services
            .AddSingleton<IAgents>(new AnsweringAgents())
            .AddSingleton<IBaseFiles>(new CommittedFiles())
            .AddSingleton<IWorkingFiles>(new WorkingFiles())
            .AddSingleton<IProjectsFolder>(new FixedProjectsFolder(data.Path))
            .AddSingleton<IJobRounds>(new RecordedRounds())
            .AddSingleton<IJobCatalog>(new OneJobCatalog(JobId.New(), data.Path)));
}
