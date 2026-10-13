using Avala.Projects.Contracts;
using Avala.Sdk.Events;

namespace Avala.Workbench.Board;

internal sealed class ProjectKeeper(JobBoard board) :
    IHandle<ProjectsRestored>,
    IHandle<ProjectCreated>,
    IHandle<ProjectRenamed>,
    IHandle<ProjectDeleted>,
    IHandle<ConversationMoved>
{
    public ValueTask HandleAsync(ProjectsRestored integrationEvent, CancellationToken cancellationToken) => KeepAsync(_ => integrationEvent.Map);

    public ValueTask HandleAsync(ProjectCreated integrationEvent, CancellationToken cancellationToken) =>
        KeepAsync(map => map with { Projects = map.Projects.RemoveAll(project => project.Project == integrationEvent.Project.Project).Add(integrationEvent.Project) });

    public ValueTask HandleAsync(ProjectRenamed integrationEvent, CancellationToken cancellationToken) =>
        KeepAsync(map => map with { Projects = [.. map.Projects.Select(project => project.Project == integrationEvent.Project.Project ? integrationEvent.Project : project)] });

    public ValueTask HandleAsync(ProjectDeleted integrationEvent, CancellationToken cancellationToken) =>
        KeepAsync(map => new ProjectMap(
            map.Projects.RemoveAll(project => project.Project == integrationEvent.Project),
            map.Placements.RemoveRange(map.Placements.Where(placement => placement.Value == integrationEvent.Project).Select(placement => placement.Key))));

    public ValueTask HandleAsync(ConversationMoved integrationEvent, CancellationToken cancellationToken) =>
        KeepAsync(map => map with { Placements = map.Placements.SetItem(integrationEvent.Job, integrationEvent.Project) });

    private ValueTask KeepAsync(Func<ProjectMap, ProjectMap> change)
    {
        board.Publish(change(board.Projects));

        return ValueTask.CompletedTask;
    }
}
