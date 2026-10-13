using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Sdk;
using Avala.Sdk.Domain;

namespace Avala.Projects.Catalog;

internal readonly record struct RepositoryFolder(string Name, string Parent);

internal sealed record JobPlaced(JobId Job, ProjectId Project) : IDomainEvent;

internal sealed class Project : IAggregateRoot<ProjectId>
{
    private Project(ProjectId id, string name, Option<string> repository)
    {
        Id = id;
        Name = name;
        Repository = repository;
    }

    public ProjectId Id { get; }

    public string Name { get; }

    public Option<string> Repository { get; }

    public ProjectRecord Record => new(Id, Name, Repository);

    public static Result<Project, ProjectError> ForRepository(ProjectId id, string repository, RepositoryFolder folder, IReadOnlyCollection<string> taken) =>
        new Project(id, FreeName(folder, taken), repository);

    public static Result<Project, ProjectError> Restore(ProjectRecord record) =>
        new Project(record.Project, record.Name, record.Repository);

    public Result<JobPlaced, ProjectError> Place(JobId job, Option<JobId> parent) =>
        parent.IsSome ? ProjectError.FollowsItsParent : new JobPlaced(job, Id);

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
