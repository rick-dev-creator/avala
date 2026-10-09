using Avala.Delegation.Contracts;
using Avala.Delegation.Records;
using Avala.Sdk;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Delegation.Storage;

internal sealed class SqliteDelegationStore(AvalaPaths paths) : IDelegationStore, IStartupTask, IAsyncDisposable
{
    private readonly SerialExecutor serial = new();
    private readonly HashSet<int> written = [];
    private DelegationDbContext? context;

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

    public Task RunAsync(CancellationToken cancellationToken) => RunAsync(_ => Task.FromResult(true), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await serial.DisposeAsync();

        if (context is not null)
        {
            await context.DisposeAsync();
        }
    }

    private Task<T> RunAsync<T>(Func<DelegationDbContext, Task<T>> work, CancellationToken cancellationToken) =>
        serial.RunAsync(token => Task.Run(async () => await work(await OpenAsync(token)), token), cancellationToken);

    private async Task<DelegationDbContext> OpenAsync(CancellationToken cancellationToken)
    {
        if (context is null)
        {
            Directory.CreateDirectory(paths.Data);
            context = new DelegationDbContext(paths.Database("delegation"));
            await ModuleDatabase.MigrateAsync(context, cancellationToken);
        }

        return context;
    }
}
