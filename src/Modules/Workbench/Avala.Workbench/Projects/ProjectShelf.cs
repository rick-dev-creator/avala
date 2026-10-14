using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Sdk;
using Avala.Sdk.Appearance;

namespace Avala.Workbench.Projects;

internal sealed class ProjectShelf(IProjects projects, IWindowLayout layout)
{
    public const string NewProjectName = "New project";

    public async Task<Result<ProjectId, ProjectRejection>> CreateAsync(CancellationToken cancellationToken)
    {
        var taken = projects.Current.Projects.Select(project => project.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = Enumerable.Range(1, taken.Count + 1).Select(number => number == 1 ? NewProjectName : $"{NewProjectName} {number}").First(candidate => !taken.Contains(candidate));

        return (await projects.CreateAsync(name, cancellationToken)).Map(created => created.Project.Project);
    }

    public async Task<Option<ProjectRejection>> RenameAsync(ProjectId project, string name, CancellationToken cancellationToken) =>
        Refusal(await projects.RenameAsync(project, name, cancellationToken));

    public async Task<Option<ProjectRejection>> DeleteAsync(ProjectId project, CancellationToken cancellationToken) =>
        Refusal(await projects.DeleteAsync(project, cancellationToken));

    public async Task<Option<ProjectRejection>> MoveAsync(JobId job, ProjectId project, CancellationToken cancellationToken) =>
        Refusal(await projects.MoveAsync(job, project, cancellationToken));

    public async Task<Option<ProjectRejection>> ArchiveAsync(IReadOnlyList<JobId> jobs, CancellationToken cancellationToken) =>
        Refusal(await projects.ArchiveAsync(jobs, cancellationToken));

    public async Task<Option<ProjectRejection>> RestoreAsync(JobId job, CancellationToken cancellationToken) =>
        Refusal(await projects.RestoreAsync(job, cancellationToken));

    public async Task<ImmutableHashSet<string>> CollapsedAsync(CancellationToken cancellationToken) =>
        [.. (await layout.ReadAsync(cancellationToken)).CollapsedGroups];

    public ValueTask RememberAsync(string group, bool collapsed, CancellationToken cancellationToken) =>
        layout.RememberAsync(
            remembered => remembered with
            {
                CollapsedGroups = collapsed ? remembered.CollapsedGroups.Remove(group).Add(group) : remembered.CollapsedGroups.Remove(group),
            },
            cancellationToken);

    private static Option<ProjectRejection> Refusal<T>(Result<T, ProjectRejection> result) =>
        result.Match(_ => Option<ProjectRejection>.None, rejection => rejection);
}
