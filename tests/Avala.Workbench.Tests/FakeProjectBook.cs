using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Tests;

internal sealed class FakeProjectBook : IProjects
{
    public ProjectMap Current { get; set; } = ProjectMap.Empty;

    public List<string> Calls { get; } = [];

    public Option<ProjectRejection> Refusal { get; set; }

    public ProjectRecord Add(string name, string? repository = null)
    {
        var project = new ProjectRecord(ProjectId.New(), name, repository is null ? Option<string>.None : repository);
        Current = Current with { Projects = Current.Projects.Add(project) };

        return project;
    }

    public ValueTask<Result<ProjectCreated, ProjectRejection>> CreateAsync(string name, CancellationToken cancellationToken)
    {
        Calls.Add($"create {name}");

        return DecidedAsync(() => new ProjectCreated(Add(name)));
    }

    public ValueTask<Result<ProjectRenamed, ProjectRejection>> RenameAsync(ProjectId project, string name, CancellationToken cancellationToken)
    {
        Calls.Add($"rename {name}");

        return DecidedAsync(() =>
        {
            var renamed = Current.Find(project).Match(found => found with { Name = name }, () => new ProjectRecord(project, name, Option<string>.None));
            Current = Current with { Projects = [.. Current.Projects.Select(found => found.Project == project ? renamed : found)] };

            return new ProjectRenamed(renamed);
        });
    }

    public ValueTask<Result<ProjectDeleted, ProjectRejection>> DeleteAsync(ProjectId project, CancellationToken cancellationToken)
    {
        Calls.Add($"delete {Current.Find(project).Match(found => found.Name, () => "unknown")}");

        return DecidedAsync(() =>
        {
            Current = new ProjectMap(Current.Projects.RemoveAll(found => found.Project == project), Current.Placements.RemoveRange(Current.Placements.Where(placement => placement.Value == project).Select(placement => placement.Key)));

            return new ProjectDeleted(project);
        });
    }

    public ValueTask<Result<ConversationMoved, ProjectRejection>> MoveAsync(JobId job, ProjectId project, CancellationToken cancellationToken)
    {
        Calls.Add($"move to {Current.Find(project).Match(found => found.Name, () => "unknown")}");

        return DecidedAsync(() =>
        {
            Current = Current with { Placements = Current.Placements.SetItem(job, project) };

            return new ConversationMoved(job, project);
        });
    }

    public ValueTask<Result<ConversationsArchived, ProjectRejection>> ArchiveAsync(IReadOnlyList<JobId> jobs, CancellationToken cancellationToken)
    {
        Calls.Add($"archive {jobs.Count}");

        return DecidedAsync(() =>
        {
            Current = Current with { Archived = Current.Archived.Union(jobs) };

            return new ConversationsArchived([.. jobs]);
        });
    }

    public ValueTask<Result<ConversationRestored, ProjectRejection>> RestoreAsync(JobId job, CancellationToken cancellationToken)
    {
        Calls.Add("restore");

        return DecidedAsync(() =>
        {
            Current = Current with { Archived = Current.Archived.Remove(job) };

            return new ConversationRestored(job);
        });
    }

    private ValueTask<Result<T, ProjectRejection>> DecidedAsync<T>(Func<T> succeed) =>
        ValueTask.FromResult(Refusal.Match(Result<T, ProjectRejection>.Failure, () => Result<T, ProjectRejection>.Success(succeed())));
}
