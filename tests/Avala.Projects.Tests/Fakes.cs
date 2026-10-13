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

    public JobId Add(string repository, Option<JobId> parent = default)
    {
        var job = new JobId(Guid.CreateVersion7());
        jobs.Add(new JobSummary(job, repository, "Fix the rounding", Samples.Monday, JobStatus.Running, default, default, default) { Parent = parent });

        return job;
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

    public Task<ProjectMap> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(new ProjectMap(Projects, Placements));

    public Task KeepAsync(ProjectRecord project, CancellationToken cancellationToken)
    {
        Projects = Projects.RemoveAll(kept => kept.Project == project.Project).Add(project);

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

    public Projected(MemoryStore? store = null)
    {
        Store = store ?? new MemoryStore();
        book = new ProjectBook(Store, Bus);
        Repositories = new RepositoryProjects(book, Catalog, new FakeScratch());
        Entry = new ProjectsEntry(book, Catalog);
    }

    public MemoryStore Store { get; }

    public FakeCatalog Catalog { get; } = new();

    public RecordingBus Bus { get; } = new();

    public RepositoryProjects Repositories { get; }

    public ProjectsEntry Entry { get; }

    public IReadOnlyList<ProjectRecord> Created => [.. Bus.Published.OfType<ProjectCreated>().Select(created => created.Project)];

    public async Task<JobId> SubmitAsync(string repository, Option<JobId> parent = default)
    {
        var job = Catalog.Add(repository, parent);
        await Repositories.HandleAsync(new JobSubmitted(job), Samples.Cancellation);

        return job;
    }

    public ValueTask DisposeAsync() => book.DisposeAsync();
}
