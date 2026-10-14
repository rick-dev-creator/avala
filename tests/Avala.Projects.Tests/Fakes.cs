using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Projects.Placing;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Projects.Tests;

internal static class Samples
{
    public const string Ledger = "/home/rick/code/ledger";

    public const string Scratch = "/data/scratch/2026-10-13-sketch-a-parser-3f9a1c";

    public static DateTimeOffset Monday { get; } = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    public static CancellationToken Cancellation => TestContext.Current.CancellationToken;
}

internal sealed class FakeCatalog : IJobCatalog
{
    private readonly List<JobSummary> jobs = [];

    public JobId Add(string repository, Option<JobId> parent = default, JobStatus status = JobStatus.Running)
    {
        var job = new JobId(Guid.CreateVersion7());
        jobs.Add(new JobSummary(job, repository, "Fix the rounding", Samples.Monday, status, default, default, default) { Parent = parent });

        return job;
    }

    public void Set(JobId job, JobStatus status)
    {
        var index = jobs.FindIndex(summary => summary.Job == job);
        jobs[index] = jobs[index] with { Status = status };
    }

    public ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<JobSummary>>([.. jobs]);

    public ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(jobs.Find(summary => summary.Job == job) is { } found ? Option<JobHistory>.Some(new JobHistory(found, [], [])) : Option<JobHistory>.None);

    public ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<JobSummary>>([.. jobs.Where(summary => summary.Parent == parent)]);

    public ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken) => ValueTask.FromResult(Option<JobTree>.None);
}

internal sealed class FakeScratch : IScratchRepositories
{
    public string Folder => "/data/scratch";

    public bool Holds(string repository) => repository.StartsWith($"{Folder}/", StringComparison.Ordinal);

    public ValueTask<Result<string, WorkspaceFailure>> CreateAsync(string instruction, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<string, WorkspaceFailure>.Success(Samples.Scratch));
}

internal sealed class MemoryStore : IProjectStore
{
    public ImmutableList<ProjectRecord> Projects { get; private set; } = [];

    public ImmutableDictionary<JobId, ProjectId> Placements { get; private set; } = ImmutableDictionary<JobId, ProjectId>.Empty;

    public ImmutableHashSet<JobId> Archived { get; private set; } = [];

    public Task<ProjectMap> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(new ProjectMap(Projects, Placements) { Archived = Archived });

    public Task ArchiveAsync(IReadOnlyCollection<JobId> jobs, CancellationToken cancellationToken)
    {
        Archived = Archived.Union(jobs);

        return Task.CompletedTask;
    }

    public Task RestoreAsync(JobId job, CancellationToken cancellationToken)
    {
        Archived = Archived.Remove(job);

        return Task.CompletedTask;
    }

    public Task KeepAsync(ProjectRecord project, CancellationToken cancellationToken)
    {
        Projects = Projects.RemoveAll(kept => kept.Project == project.Project).Add(project);

        return Task.CompletedTask;
    }

    public Task RemoveAsync(ProjectId project, CancellationToken cancellationToken)
    {
        Projects = Projects.RemoveAll(kept => kept.Project == project);
        Placements = Placements.RemoveRange(Placements.Where(placement => placement.Value == project).Select(placement => placement.Key));

        return Task.CompletedTask;
    }

    public Task PlaceAsync(JobId job, ProjectId project, CancellationToken cancellationToken)
    {
        Placements = Placements.SetItem(job, project);

        return Task.CompletedTask;
    }
}

internal sealed class Projected : IAsyncDisposable
{
    private readonly ProjectBook book;

    public Projected(MemoryStore? store = null, FakeCatalog? catalog = null)
    {
        Store = store ?? new MemoryStore();
        Catalog = catalog ?? new FakeCatalog();
        book = new ProjectBook(Store, Bus);
        Repositories = new RepositoryProjects(book, Catalog, new FakeScratch());
        Entry = new ProjectsEntry(book, Catalog);
        Returns = new ArchiveReturns(book);
    }

    public MemoryStore Store { get; }

    public FakeCatalog Catalog { get; }

    public RecordingBus Bus { get; } = new();

    public RepositoryProjects Repositories { get; }

    public ProjectsEntry Entry { get; }

    public ArchiveReturns Returns { get; }

    public IReadOnlyList<ProjectRecord> Created => [.. Bus.Published.OfType<ProjectCreated>().Select(created => created.Project)];

    public async Task<JobId> SubmitAsync(string repository, Option<JobId> parent = default, JobStatus status = JobStatus.Running)
    {
        var job = Catalog.Add(repository, parent, status);
        await Repositories.HandleAsync(new JobSubmitted(job), Samples.Cancellation);

        return job;
    }

    public ValueTask DisposeAsync() => book.DisposeAsync();
}
