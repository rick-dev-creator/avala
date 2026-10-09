using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Provisioning;
using Avala.Workspaces.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workspaces.Tests;

public sealed class WorkspacesPluginTests
{
    [Fact]
    public async Task TheRegistrationResolvesEveryServiceItRegistersAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Equal(composition.Registered, composition.ResolveEveryRegistration());
    }

    [Fact]
    public async Task TheStoreIsOneInstanceUnderItsContractAndWorktreesLiveInTheDataFolderAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Same(composition.Get<SqliteWorkspaceStore>(), composition.Get<IWorkspaceStore>());
        Assert.Same(composition.Get<SqliteWorkspaceStore>(), composition.Get<IStartupTask>());
        Assert.Equal(new AvalaPaths(data.Path).Folder("worktrees").Canonical(), composition.Get<WorkspaceSettings>().Root);
    }

    private static PluginComposition Compose(TemporaryFolder data) =>
        PluginComposition.Of(new WorkspacesPlugin(), new AvalaPaths(data.Path), services => services
            .AddSingleton(new ServiceCollection().AddRuntime(new AvalaPaths(data.Path)).BuildServiceProvider().GetRequiredService<IProcessRunner>()));
}
