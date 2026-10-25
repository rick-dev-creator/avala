using Avala.Sdk;
using Avala.Storage;
using Avala.Verification.Contracts;
using Avala.Verification.Discovering;
using Avala.Verification.Evidence;
using Microsoft.EntityFrameworkCore;

namespace Avala.Verification.Storage;

internal sealed class SqliteEvidenceStore(AvalaPaths paths) : IEvidenceStore, IDiscoveryStore, IStartupTask, IAsyncDisposable
{
    private readonly DatabaseOwner<VerificationDbContext> owner = new(paths.Database("verification"), file => new VerificationDbContext(file));
    private readonly HashSet<int> written = [];

    public Task RecordAsync(VerificationReport report, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                var row = StoredReport.Of(report);
                await database.Reports.AddAsync(row, cancellationToken);
                var saved = await database.SaveChangesAsync(cancellationToken);
                written.Add(row.Key);
                database.ChangeTracker.Clear();

                return saved;
            },
            cancellationToken);

    public Task<IReadOnlyList<VerificationReport>> EarlierRunsAsync(CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<VerificationReport>>(
            async database =>
            [
                .. (await database.Reports.AsNoTracking().OrderBy(row => row.Key).ToListAsync(cancellationToken))
                    .Where(row => !written.Contains(row.Key))
                    .Select(row => row.Read()),
            ],
            cancellationToken);

    public Task<IReadOnlyList<Discovery>> DiscoveriesAsync(CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<Discovery>>(
            async database => [.. (await database.Discoveries.AsNoTracking().ToListAsync(cancellationToken)).Select(row => row.Read())],
            cancellationToken);

    public Task KeepAsync(Discovery discovery, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                var row = StoredDiscovery.Of(discovery);
                var stored = await database.Discoveries.FindAsync([row.Job], cancellationToken);

                if (stored is null)
                {
                    await database.Discoveries.AddAsync(row, cancellationToken);
                }
                else
                {
                    stored.State = row.State;
                    stored.Scout = row.Scout;
                    stored.Follows = row.Follows;
                    stored.Declaration = row.Declaration;
                    stored.Proposal = row.Proposal;
                    stored.Reads = row.Reads;
                    stored.Ran = row.Ran;
                    stored.Final = row.Final;
                    stored.At = row.At;
                }

                var saved = await database.SaveChangesAsync(cancellationToken);
                database.ChangeTracker.Clear();

                return saved;
            },
            cancellationToken);

    public StartupStage Stage => StartupStage.Open;

    public Task RunAsync(CancellationToken cancellationToken) => owner.OpenedAsync(cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private Task<T> RunAsync<T>(Func<VerificationDbContext, Task<T>> work, CancellationToken cancellationToken) =>
        owner.RunAsync(work, cancellationToken);
}
