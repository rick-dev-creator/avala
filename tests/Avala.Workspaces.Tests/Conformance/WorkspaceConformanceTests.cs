using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workspaces.Tests.Conformance;

public sealed class WorkspaceConformanceTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheWorkspacesModuleKeepsTheWorkspaceContractAsync()
    {
        await using var data = new TemporaryFolder();
        var processes = new ServiceCollection().AddRuntime(new AvalaPaths(data.Path)).BuildServiceProvider().GetRequiredService<IProcessRunner>();
        await using var composition = PluginComposition.Of(new WorkspacesPlugin(), new AvalaPaths(data.Path), services => services
            .AddSingleton(TimeProvider.System)
            .AddSingleton(processes));
        await using var repository = await TemporaryRepository.CreateAsync(processes, Cancellation);
        _ = await repository.GitAsync(Cancellation, "config", "user.name", "Test");
        _ = await repository.GitAsync(Cancellation, "config", "user.email", "test@localhost");

        foreach (var (path, content) in WorkspaceConformance.Seed.Where(file => file.Path != "README.md"))
        {
            await repository.CommitAsync(path, content, Cancellation);
        }

        foreach (var task in composition.All<IStartupTask>())
        {
            await task.RunAsync(Cancellation);
        }

        Assert.Empty(await WorkspaceConformance.CheckAsync(Services(composition), repository.Path, Cancellation));
    }

    [Fact]
    public async Task TheInMemoryWorkspacesKeepTheWorkspaceContractAsync()
    {
        await using var data = new TemporaryFolder();
        await using var folder = new TemporaryFolder();
        var git = new InMemoryGit();
        await using var composition = PluginComposition.Of(new InMemoryWorkspacesPlugin(git), new AvalaPaths(data.Path), services => services.AddSingleton(TimeProvider.System));
        var repository = Path.Combine(folder.Path, "repository");
        await git.CreateAsync(repository, WorkspaceConformance.Seed, Cancellation);

        Assert.Empty(await WorkspaceConformance.CheckAsync(Services(composition), repository, Cancellation));
    }

    private static WorkspaceServices Services(PluginComposition composition) =>
        new(
            composition.Get<IWorkspaces>(),
            composition.Get<IWorkspaceChanges>(),
            composition.Get<IWorkspaceCheckpoints>(),
            composition.Get<IBaseFiles>(),
            composition.Get<IWorkingFiles>(),
            composition.Get<IWorkingCommits>());
}
