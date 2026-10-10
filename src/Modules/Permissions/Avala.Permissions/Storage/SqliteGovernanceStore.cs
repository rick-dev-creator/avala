using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Sdk;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Permissions.Storage;

internal sealed class SqliteGovernanceStore(AvalaPaths paths) : IGovernanceStore, IStartupTask, IAsyncDisposable
{
    private readonly DatabaseOwner<PermissionsDbContext> owner = new(paths.Database("permissions"), file => new PermissionsDbContext(file));
    private readonly HashSet<int> written = [];

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

    public Task RecordAsync(EndedJob ended, CancellationToken cancellationToken) =>
        AddAsync(StoredFact.Of(FactKind.Ended, new SessionId(Guid.Empty), ended.Job, DateTimeOffset.MinValue, ended), cancellationToken);

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
                    [.. rows[FactKind.Answer].Select(row => row.Read<HumanAnswer>())])
                {
                    Ended = rows[FactKind.Ended].Select(row => row.Read<EndedJob>().Job).ToHashSet(),
                };
            },
            cancellationToken);

    public Task RunAsync(CancellationToken cancellationToken) => owner.OpenedAsync(cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

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
        owner.RunAsync(work, cancellationToken);
}
