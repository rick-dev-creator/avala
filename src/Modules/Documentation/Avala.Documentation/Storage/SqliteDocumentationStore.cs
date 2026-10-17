using Avala.Documentation.Contracts;
using Avala.Documentation.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Documentation.Storage;

internal sealed class StoredChoice
{
    public Guid Job { get; init; }

    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string LastPlan { get; set; } = string.Empty;
}

internal sealed class StoredVersion
{
    public Guid Job { get; init; }

    public int Number { get; init; }

    public string Kind { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public DateTimeOffset At { get; init; }

    public Guid Writer { get; init; }

    public int Attempt { get; init; }

    public FeatureVersion Read() =>
        new(Number, Enum.Parse<VersionKind>(Kind), Path, At) { Writer = Writer == Guid.Empty ? Option<JobId>.None : new JobId(Writer), Attempt = Math.Max(1, Attempt) };
}

internal sealed class StoredWriter
{
    public Guid Writer { get; init; }

    public Guid Job { get; init; }

    public int Number { get; init; }

    public string Kind { get; init; } = string.Empty;

    public string Plan { get; init; } = string.Empty;
}

internal sealed class StoredRequest
{
    public Guid Job { get; init; }

    public int Number { get; init; }

    public string Kind { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    public DateTimeOffset At { get; init; }

    public int Attempt { get; init; }

    public int Version { get; set; }

    public ChangeRequest Read() =>
        new(Number, Enum.Parse<RequestKind>(Kind), Text, At, Attempt) { Version = Version == 0 ? Option<int>.None : Version };
}

internal sealed class SqliteDocumentationStore(AvalaPaths paths) : IDocumentationStore, IStartupTask, IAsyncDisposable
{
    private readonly DatabaseOwner<DocumentationDbContext> owner = new(paths.Database("documentation"), file => new DocumentationDbContext(file));

    public StartupStage Stage => StartupStage.Open;

    public Task RunAsync(CancellationToken cancellationToken) => owner.OpenedAsync(cancellationToken);

    public Task<IReadOnlyList<DocumentedJob>> ReadAsync(CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                var choices = await database.Choices.AsNoTracking().ToListAsync(cancellationToken);
                var versions = (await database.Versions.AsNoTracking().ToListAsync(cancellationToken)).ToLookup(row => row.Job);
                var writers = (await database.Writers.AsNoTracking().ToListAsync(cancellationToken)).ToLookup(row => row.Job);
                var requests = (await database.Requests.AsNoTracking().ToListAsync(cancellationToken)).ToLookup(row => row.Job);

                return (IReadOnlyList<DocumentedJob>)[.. choices.Select(row => new DocumentedJob(
                    new JobId(row.Job),
                    row.Slug.Length == 0 ? FeatureChoice.Undocumented : FeatureChoice.Of(new Feature(row.Slug, row.Title)))
                {
                    Versions = [.. versions[row.Job].OrderBy(version => version.Number).Select(version => version.Read())],
                    Writer = writers[row.Job].Select(writer => new PendingWriter(new JobId(writer.Writer), writer.Number, Enum.Parse<VersionKind>(writer.Kind), writer.Plan)).FirstOrDefault().ToOption(),
                    LastPlan = row.LastPlan,
                    Requests = [.. requests[row.Job].OrderBy(request => request.Number).Select(request => request.Read())],
                })];
            },
            cancellationToken);

    public Task ChooseAsync(JobId job, FeatureChoice choice, CancellationToken cancellationToken) =>
        ChangeAsync(job, row =>
        {
            row.Slug = choice.Feature.Match(feature => feature.Slug, () => string.Empty);
            row.Title = choice.Feature.Match(feature => feature.Title, () => string.Empty);
        }, cancellationToken);

    public Task WritingAsync(JobId job, PendingWriter writer, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                await database.Writers.AddAsync(new StoredWriter { Writer = writer.Writer.Value, Job = job.Value, Number = writer.Number, Kind = writer.Kind.ToString(), Plan = writer.Plan }, cancellationToken);

                return await SaveAsync(database, cancellationToken);
            },
            cancellationToken);

    public async Task WrittenAsync(JobId job, FeatureVersion version, string plan, CancellationToken cancellationToken)
    {
        await owner.RunAsync(
            async database =>
            {
                await database.Writers.Where(row => row.Job == job.Value).ExecuteDeleteAsync(cancellationToken);

                if (version.Kind != VersionKind.AsBuilt)
                {
                    await database.Requests.Where(row => row.Job == job.Value && row.Version == 0).ExecuteUpdateAsync(row => row.SetProperty(request => request.Version, version.Number), cancellationToken);
                }
                await database.Versions.AddAsync(
                    new StoredVersion
                    {
                        Job = job.Value,
                        Number = version.Number,
                        Kind = version.Kind.ToString(),
                        Path = version.Path,
                        At = version.At,
                        Writer = version.Writer.Match(writer => writer.Value, () => Guid.Empty),
                        Attempt = version.Attempt,
                    },
                    cancellationToken);

                return await SaveAsync(database, cancellationToken);
            },
            cancellationToken);
        await ChangeAsync(job, row => row.LastPlan = plan, cancellationToken);
    }

    public Task RequestedAsync(JobId job, ChangeRequest request, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                await database.Requests.AddAsync(
                    new StoredRequest { Job = job.Value, Number = request.Number, Kind = request.Kind.ToString(), Text = request.Text, At = request.At, Attempt = request.Attempt },
                    cancellationToken);

                return await SaveAsync(database, cancellationToken);
            },
            cancellationToken);

    public async Task ForgetAsync(JobId job, JobId writer, string plan, CancellationToken cancellationToken)
    {
        await owner.RunAsync(database => database.Writers.Where(row => row.Writer == writer.Value).ExecuteDeleteAsync(cancellationToken), cancellationToken);
        await ChangeAsync(job, row => row.LastPlan = plan, cancellationToken);
    }

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private Task<int> ChangeAsync(JobId job, Action<StoredChoice> change, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                if (await database.Choices.FindAsync([job.Value], cancellationToken) is not { } row)
                {
                    row = new StoredChoice { Job = job.Value };
                    await database.Choices.AddAsync(row, cancellationToken);
                }

                change(row);

                return await SaveAsync(database, cancellationToken);
            },
            cancellationToken);

    private static async Task<int> SaveAsync(DocumentationDbContext database, CancellationToken cancellationToken)
    {
        var saved = await database.SaveChangesAsync(cancellationToken);
        database.ChangeTracker.Clear();

        return saved;
    }
}
