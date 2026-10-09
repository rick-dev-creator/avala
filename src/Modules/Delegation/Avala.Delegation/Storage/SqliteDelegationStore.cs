using Avala.Delegation.Contracts;
using Avala.Delegation.Records;
using Avala.Sdk;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Delegation.Storage;

internal sealed class SqliteDelegationStore(AvalaPaths paths) : IDelegationStore, IStartupTask, IAsyncDisposable
{
    private readonly DatabaseOwner<DelegationDbContext> owner = new(paths.Database("delegation"), file => new DelegationDbContext(file));
    private readonly HashSet<int> written = [];

    public Task RecordAsync(DelegationRecord record, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                var row = StoredRecord.Of(record);
                await database.Records.AddAsync(row, cancellationToken);
                var saved = await database.SaveChangesAsync(cancellationToken);
                written.Add(row.Key);
                database.ChangeTracker.Clear();

                return saved;
            },
            cancellationToken);

    public Task<IReadOnlyList<DelegationRecord>> EarlierRunsAsync(CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<DelegationRecord>>(
            async database =>
            [
                .. (await database.Records.AsNoTracking().OrderBy(row => row.Key).ToListAsync(cancellationToken))
                    .Where(row => !written.Contains(row.Key))
                    .GroupBy(row => (row.Session, row.Item))
                    .Select(versions => versions.Last().Read()),
            ],
            cancellationToken);

    public Task RunAsync(CancellationToken cancellationToken) => owner.OpenedAsync(cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private Task<T> RunAsync<T>(Func<DelegationDbContext, Task<T>> work, CancellationToken cancellationToken) =>
        owner.RunAsync(work, cancellationToken);
}
