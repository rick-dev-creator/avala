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
    public static ProjectMap Empty { get; } = new([], ImmutableDictionary<JobId, ProjectId>.Empty);

    public Option<ProjectRecord> Of(JobId job, string repository) =>
        Placements.TryGetValue(job, out var placed) && Projects.Find(project => project.Project == placed) is { } moved
            ? moved
            : Projects.Find(project => project.Repository == repository) is { } own ? own : Option<ProjectRecord>.None;
}

public interface IProjects
{
    ProjectMap Current { get; }

    ValueTask<Result<ConversationMoved, ProjectRejection>> MoveAsync(JobId job, ProjectId project, CancellationToken cancellationToken);
}

public enum ProjectRejection
{
    UnknownProject,
    UnknownJob,
    FollowsItsParent,
}
