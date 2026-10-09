using Avala.Sdk;
using Avala.Storage;
using Avala.Verification.Contracts;
using Avala.Verification.Evidence;
using Microsoft.EntityFrameworkCore;

namespace Avala.Verification.Storage;

internal sealed class SqliteEvidenceStore(AvalaPaths paths) : IEvidenceStore, IStartupTask, IAsyncDisposable
{
    private readonly SerialExecutor serial = new();
    private readonly HashSet<int> written = [];
    private VerificationDbContext? context;

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

    public Task RunAsync(CancellationToken cancellationToken) => RunAsync(_ => Task.FromResult(true), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await serial.DisposeAsync();

        if (context is not null)
        {
            await context.DisposeAsync();
        }
    }

    private Task<T> RunAsync<T>(Func<VerificationDbContext, Task<T>> work, CancellationToken cancellationToken) =>
        serial.RunAsync(token => Task.Run(async () => await work(await OpenAsync(token)), token), cancellationToken);

    private async Task<VerificationDbContext> OpenAsync(CancellationToken cancellationToken)
    {
        if (context is null)
        {
            Directory.CreateDirectory(paths.Data);
            context = new VerificationDbContext(paths.Database("verification"));
            await ModuleDatabase.MigrateAsync(context, cancellationToken);
        }

        return context;
    }
}
