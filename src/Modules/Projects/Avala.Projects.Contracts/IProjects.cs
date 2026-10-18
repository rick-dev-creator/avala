using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Projects.Contracts;

public readonly record struct ProjectId(Guid Value)
{
    public static ProjectId New() => new(Guid.CreateVersion7());
}

public sealed record ProjectRecord(ProjectId Project, string Name, Option<string> Repository);

public sealed record ProjectMap(ImmutableList<ProjectRecord> Projects, ImmutableDictionary<JobId, ProjectId> Placements)
{
    public const int LongestName = 60;

    public static ProjectMap Empty { get; } = new([], ImmutableDictionary<JobId, ProjectId>.Empty);

    public ImmutableHashSet<JobId> Archived { get; init; } = [];

    public Option<ProjectRecord> Find(ProjectId project) =>
        Projects.Find(found => found.Project == project) is { } known ? known : Option<ProjectRecord>.None;

    public Option<ProjectRecord> Of(JobId job, string repository) =>
        Placements.TryGetValue(job, out var placed) && Projects.Find(project => project.Project == placed) is { } moved
            ? moved
            : Projects.Find(project => project.Repository == repository) is { } own ? own : Option<ProjectRecord>.None;
}

public interface IProjects
{
    ProjectMap Current { get; }

    ValueTask<Result<ProjectCreated, ProjectRejection>> CreateAsync(string name, CancellationToken cancellationToken);

    ValueTask<Result<ProjectRenamed, ProjectRejection>> RenameAsync(ProjectId project, string name, CancellationToken cancellationToken);

    ValueTask<Result<ProjectDeleted, ProjectRejection>> DeleteAsync(ProjectId project, CancellationToken cancellationToken);

    ValueTask<Result<ConversationMoved, ProjectRejection>> MoveAsync(JobId job, ProjectId project, CancellationToken cancellationToken);

    ValueTask<Result<ConversationsArchived, ProjectRejection>> ArchiveAsync(IReadOnlyList<JobId> jobs, CancellationToken cancellationToken);

    ValueTask<Result<ConversationRestored, ProjectRejection>> RestoreAsync(JobId job, CancellationToken cancellationToken);
}

public enum ProjectRejection
{
    NameEmpty,
    NameTooLong,
    NameTaken,
    UnknownProject,
    UnknownJob,
    NotEmpty,
    FollowsItsParent,
    StillWorking,
    NotArchived,
}
