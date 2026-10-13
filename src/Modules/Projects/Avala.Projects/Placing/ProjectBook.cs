using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Projects.Catalog;
using Avala.Projects.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Projects.Placing;

internal interface IProjectStore
{
    Task<ProjectMap> ReadAsync(CancellationToken cancellationToken);

    Task KeepAsync(ProjectRecord project, CancellationToken cancellationToken);

    Task PlaceAsync(JobId job, ProjectId project, CancellationToken cancellationToken);
}

internal sealed class ProjectBook(IProjectStore store, IEventBus bus) : IAsyncDisposable
{
    private readonly SerialExecutor owner = new();
    private readonly Dictionary<ProjectId, Project> projects = [];
    private ProjectMap map = ProjectMap.Empty;

    public ProjectMap Current => Volatile.Read(ref map);

    public IReadOnlyCollection<string> Names => [.. projects.Values.Select(project => project.Name)];

    public Task WorkAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken) => owner.RunAsync(work, cancellationToken);

    public Task<T> WorkAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken) => owner.RunAsync(work, cancellationToken);

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var stored = await store.ReadAsync(cancellationToken);

        foreach (var restored in stored.Projects.Select(Project.Restore))
        {
            if (restored.TryGetValue(out var project, out _))
            {
                projects[project.Id] = project;
            }
        }

        Volatile.Write(ref map, stored with { Projects = [.. projects.Values.Select(project => project.Record)] });
    }

    public Option<Project> Find(ProjectId project) => projects.TryGetValue(project, out var found) ? found : Option<Project>.None;

    public bool Holds(string repository) => projects.Values.Any(project => project.Repository == repository);

    public async Task OpenAsync(Project project, CancellationToken cancellationToken)
    {
        await store.KeepAsync(project.Record, cancellationToken);
        projects[project.Id] = project;
        Volatile.Write(ref map, Current with { Projects = Current.Projects.Add(project.Record) });
        await bus.PublishAsync(new ProjectCreated(project.Record), cancellationToken);
    }

    public async Task<ConversationMoved> PlaceAsync(JobPlaced placed, CancellationToken cancellationToken)
    {
        await store.PlaceAsync(placed.Job, placed.Project, cancellationToken);
        Volatile.Write(ref map, Current with { Placements = Current.Placements.SetItem(placed.Job, placed.Project) });
        var moved = new ConversationMoved(placed.Job, placed.Project);
        await bus.PublishAsync(moved, cancellationToken);

        return moved;
    }

    public ValueTask AnnounceAsync(CancellationToken cancellationToken) => bus.PublishAsync(new ProjectsRestored(Current), cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();
}
