using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workspaces.Tests.Conformance;

internal sealed class WorkspaceBench : IAsyncDisposable
{
    private readonly TemporaryFolder data;
    private readonly PluginComposition composition;
    private readonly TemporaryRepository repository;

    private WorkspaceBench(TemporaryFolder data, PluginComposition composition, TemporaryRepository repository)
    {
        this.data = data;
        this.composition = composition;
        this.repository = repository;
    }

    public FidelityBench Bench => new(
        new WorkspaceServices(
            composition.Get<IWorkspaces>(),
            composition.Get<IWorkspaceChanges>(),
            composition.Get<IWorkspaceCheckpoints>(),
            composition.Get<IWorkspaceShelf>(),
            composition.Get<IBaseFiles>(),
            composition.Get<IWorkingFiles>(),
            composition.Get<IWorkingCommits>()),
        composition.Get<IRepositories>(),
        composition.Get<IRemoteRepositories>(),
        composition.Get<IRepositoryIndex>(),
        repository.Path,
        (path, content, token) => repository.CommitAsync(path, content, token));

    public static async Task<WorkspaceBench> CreateAsync(bool realGit, CancellationToken cancellationToken)
    {
        var data = new TemporaryFolder();
        var processes = new ServiceCollection().AddRuntime(new AvalaPaths(data.Path)).BuildServiceProvider().GetRequiredService<IProcessRunner>();

        if (!realGit)
        {
            var git = new InMemoryGit();
            var memory = PluginComposition.Of(new InMemoryWorkspacesPlugin(git), new AvalaPaths(data.Path), services => services.AddSingleton(TimeProvider.System));
            var held = await TemporaryRepository.InMemoryAsync(git, processes, "repository", cancellationToken);

            return await SeededAsync(new WorkspaceBench(data, memory, held), cancellationToken);
        }

        var composition = PluginComposition.Of(new WorkspacesPlugin(), new AvalaPaths(data.Path), services => services
            .AddSingleton(TimeProvider.System)
            .AddSingleton(processes));
        var repository = await TemporaryRepository.CreateAsync(processes, cancellationToken);
        _ = await repository.GitAsync(cancellationToken, "config", "user.name", "Test");
        _ = await repository.GitAsync(cancellationToken, "config", "user.email", "test@localhost");

        foreach (var task in composition.All<IStartupTask>())
        {
            await task.RunAsync(cancellationToken);
        }

        return await SeededAsync(new WorkspaceBench(data, composition, repository), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await composition.DisposeAsync();
        await repository.DisposeAsync();
        await data.DisposeAsync();
    }

    private static async Task<WorkspaceBench> SeededAsync(WorkspaceBench bench, CancellationToken cancellationToken)
    {
        foreach (var (path, content) in WorkspaceConformance.Seed.Where(file => file.Path != "README.md"))
        {
            await bench.repository.CommitAsync(path, content, cancellationToken);
        }

        return bench;
    }
}
