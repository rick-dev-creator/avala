using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Sdk;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Permissions.Storage;

internal sealed class SqliteGovernanceStore(AvalaPaths paths) : IGovernanceStore, IStartupTask, IAsyncDisposable
{
    private readonly SerialExecutor serial = new();
    private readonly HashSet<int> written = [];
    private PermissionsDbContext? context;

    public Task RecordAsync(SessionPolicy policy, CancellationToken cancellationToken) =>
        AddAsync(StoredFact.Of(FactKind.Policy, policy.Session, Option<JobId>.None, DateTimeOffset.MinValue, policy), cancellationToken);

    public Task RecordAsync(SessionAutonomy autonomy, CancellationToken cancellationToken) =>
        AddAsync(StoredFact.Of(FactKind.Autonomy, autonomy.Session, autonomy.Job, DateTimeOffset.MinValue, autonomy), cancellationToken);

    public Task RecordAsync(PolicyDecision decision, CancellationToken cancellationToken) =>
        AddAsync(StoredFact.Of(FactKind.Decision, decision.Session, decision.Job, decision.At, decision), cancellationToken);

    public Task RecordAsync(FormDecision decision, CancellationToken cancellationToken) =>
        AddAsync(StoredFact.Of(FactKind.Form, decision.Session, decision.Job, decision.At, decision), cancellationToken);

    public Task RecordAsync(HumanAnswer answer, CancellationToken cancellationToken) =>
        AddAsync(StoredFact.Of(FactKind.Answer, answer.Session, answer.Job, answer.At, answer), cancellationToken);

    public Task<GovernanceHistory> EarlierRunsAsync(CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                var rows = (await database.Facts.AsNoTracking().OrderBy(row => row.Key).ToListAsync(cancellationToken))
                    .Where(row => !written.Contains(row.Key))
                    .ToLookup(row => Enum.Parse<FactKind>(row.Kind));

                return new GovernanceHistory(
                    [.. rows[FactKind.Policy].Select(row => row.Read<SessionPolicy>())],
                    [.. rows[FactKind.Autonomy].Select(row => row.Read<SessionAutonomy>())],
                    [.. rows[FactKind.Decision].Select(row => row.Read<PolicyDecision>())],
                    [.. rows[FactKind.Form].Select(row => row.Read<FormDecision>())],
                    [.. rows[FactKind.Answer].Select(row => row.Read<HumanAnswer>())]);
            },
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

    private Task<int> AddAsync(StoredFact row, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                await database.Facts.AddAsync(row, cancellationToken);
                var saved = await database.SaveChangesAsync(cancellationToken);
                written.Add(row.Key);
                database.ChangeTracker.Clear();

                return saved;
            },
            cancellationToken);

    private Task<T> RunAsync<T>(Func<PermissionsDbContext, Task<T>> work, CancellationToken cancellationToken) =>
        serial.RunAsync(token => Task.Run(async () => await work(await OpenAsync(token)), token), cancellationToken);

    private async Task<PermissionsDbContext> OpenAsync(CancellationToken cancellationToken)
    {
        if (context is null)
        {
            Directory.CreateDirectory(paths.Data);
            context = new PermissionsDbContext(paths.Database("permissions"));
            await ModuleDatabase.MigrateAsync(context, cancellationToken);
        }

        return context;
    }
}
