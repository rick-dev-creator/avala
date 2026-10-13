using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Projects.Placing;
using Avala.Sdk;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Projects.Storage;

internal sealed class StoredProject
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Repository { get; init; } = string.Empty;

    public static StoredProject Of(ProjectRecord project) => new()
    {
        Id = project.Project.Value,
        Name = project.Name,
        Repository = project.Repository.Match(path => path, () => string.Empty),
    };

    public ProjectRecord Read() =>
        new(new ProjectId(Id), Name, Repository.Length > 0 ? Repository : Option<string>.None);
}

internal sealed class StoredPlacement
{
    public Guid Job { get; init; }

    public Guid Project { get; init; }
}

internal sealed class SqliteProjectStore(AvalaPaths paths) : IProjectStore, IStartupTask, IAsyncDisposable
{
    private readonly DatabaseOwner<ProjectsDbContext> owner = new(paths.Database("projects"), file => new ProjectsDbContext(file));

    public StartupStage Stage => StartupStage.Open;

    public Task RunAsync(CancellationToken cancellationToken) => owner.OpenedAsync(cancellationToken);

    public Task<ProjectMap> ReadAsync(CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                var projects = await database.Projects.AsNoTracking().ToListAsync(cancellationToken);
                var placements = await database.Placements.AsNoTracking().ToListAsync(cancellationToken);

                return new ProjectMap(
                    [.. projects.OrderBy(row => row.Id).Select(row => row.Read())],
                    placements.ToImmutableDictionary(row => new JobId(row.Job), row => new ProjectId(row.Project)));
            },
            cancellationToken);

    public Task KeepAsync(ProjectRecord project, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                var row = StoredProject.Of(project);

                if (await database.Projects.FindAsync([row.Id], cancellationToken) is { } stored)
                {
                    database.Entry(stored).Property(kept => kept.Name).CurrentValue = row.Name;
                }
                else
                {
                    await database.Projects.AddAsync(row, cancellationToken);
                }

                var saved = await database.SaveChangesAsync(cancellationToken);
                database.ChangeTracker.Clear();

                return saved;
            },
            cancellationToken);

    public Task RemoveAsync(ProjectId project, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                await database.Placements.Where(row => row.Project == project.Value).ExecuteDeleteAsync(cancellationToken);

                return await database.Projects.Where(row => row.Id == project.Value).ExecuteDeleteAsync(cancellationToken);
            },
            cancellationToken);

    public Task PlaceAsync(JobId job, ProjectId project, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                if (await database.Placements.FindAsync([job.Value], cancellationToken) is { } stored)
                {
                    database.Entry(stored).Property(kept => kept.Project).CurrentValue = project.Value;
                }
                else
                {
                    await database.Placements.AddAsync(new StoredPlacement { Job = job.Value, Project = project.Value }, cancellationToken);
                }

                var saved = await database.SaveChangesAsync(cancellationToken);
                database.ChangeTracker.Clear();

                return saved;
            },
            cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();
}
