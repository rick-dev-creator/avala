using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Sdk;
using Avala.Sdk.Domain;

namespace Avala.Projects.Catalog;

internal readonly record struct RepositoryFolder(string Name, string Parent);

internal sealed record JobPlaced(JobId Job, ProjectId Project) : IDomainEvent;

internal sealed record ProjectNamed(ProjectRecord Project) : IDomainEvent;

internal sealed record ProjectRemoved(ProjectId Project) : IDomainEvent;

internal sealed class Project : IAggregateRoot<ProjectId>
{
    private Project(ProjectId id, string name, Option<string> repository)
    {
        Id = id;
        Name = name;
        Repository = repository;
    }

    public ProjectId Id { get; }

    public string Name { get; private set; }

    public Option<string> Repository { get; }

    public ProjectRecord Record => new(Id, Name, Repository);

    public static Result<Project, ProjectError> ForRepository(ProjectId id, string repository, RepositoryFolder folder, IReadOnlyCollection<string> taken) =>
        new Project(id, FreeName(folder, taken), repository);

    public static Result<Project, ProjectError> Create(ProjectId id, string name, IReadOnlyCollection<string> taken) =>
        Named(name, taken).Map(valid => new Project(id, valid, Option<string>.None));

    public static Result<Project, ProjectError> Restore(ProjectRecord record) =>
        new Project(record.Project, record.Name, record.Repository);

    public Result<ProjectNamed, ProjectError> Rename(string name, IReadOnlyCollection<string> taken) =>
        Named(name, [.. taken.Where(other => !string.Equals(other, Name, StringComparison.OrdinalIgnoreCase))]).Map(valid =>
        {
            Name = valid;

            return new ProjectNamed(Record);
        });

    public Result<ProjectRemoved, ProjectError> Delete(int conversations) =>
        Repository.IsSome && conversations > 0 ? ProjectError.NotEmpty : new ProjectRemoved(Id);

    public Result<JobPlaced, ProjectError> Place(JobId job, Option<JobId> parent) =>
        parent.IsSome ? ProjectError.FollowsItsParent : new JobPlaced(job, Id);

    private static Result<string, ProjectError> Named(string name, IReadOnlyCollection<string> taken)
    {
        var trimmed = name.Trim();

        return trimmed.Length == 0 ? ProjectError.NameEmpty
            : trimmed.Length > ProjectMap.LongestName ? ProjectError.NameTooLong
            : taken.Contains(trimmed, StringComparer.OrdinalIgnoreCase) ? ProjectError.NameTaken
            : trimmed;
    }

    private static string FreeName(RepositoryFolder folder, IReadOnlyCollection<string> taken)
    {
        bool Free(string name) => !taken.Contains(name, StringComparer.OrdinalIgnoreCase);

        if (Free(folder.Name))
        {
            return folder.Name;
        }

        var qualified = folder.Parent.Length > 0 ? $"{folder.Name} ({folder.Parent})" : folder.Name;

        return Enumerable.Range(1, taken.Count + 2).Select(number => number == 1 ? qualified : $"{qualified} {number}").First(Free);
    }
}
