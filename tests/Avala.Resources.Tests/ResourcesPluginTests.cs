using Avala.Agents.Contracts;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Housekeeping;
using Avala.Resources.Leasing;
using Avala.Resources.Reaping;
using Avala.Resources.Sampling;
using Avala.Resources.Tracking;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Resources.Tests;

public sealed class ResourcesPluginTests
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

        Assert.Same(composition.Get<PortLeases>(), composition.Get<IProcessEnvironment>());
        Assert.Same(composition.Get<ResourceBook>(), composition.Get<IResources>());
        Assert.Same(composition.Get<OrphanReaper>(), composition.Get<IOrphans>());
        Assert.Same(composition.Get<WorktreeHousekeeper>(), composition.Get<IWorktreeHousekeeping>());
        Assert.Equal<object>(
            [composition.Get<WorktreeHousekeeper>(), composition.Get<RetentionRecovery>(), composition.Get<ResourceSampler>()],
            composition.All<IStartupTask>());
        Assert.Same(composition.Get<ResourceTracker>(), composition.Get<IHandle<SessionOpened>>());
        Assert.Same(composition.Get<ResourceTracker>(), composition.Get<IHandle<JobProgressed>>());
    }

    private static PluginComposition Compose(TemporaryFolder data) =>
        PluginComposition.Of(new ResourcesPlugin(), new AvalaPaths(data.Path), services => services
            .AddSingleton<IWorkspaces>(new FakeWorkspaces())
            .AddSingleton<IJobCatalog>(new FakeCatalog())
            .AddSingleton<IProcessTrees>(new FakeTrees())
            .AddSingleton<IListeningPorts>(new FakeListening()));
}
