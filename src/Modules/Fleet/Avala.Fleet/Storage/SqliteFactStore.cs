using Avala.Fleet.Delivery;
using Avala.Fleet.Facts;
using Avala.Fleet.Recording;
using Avala.Fleet.Server;
using Avala.Sdk;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Fleet.Storage;

internal sealed class SqliteFactStore(AvalaPaths paths) : IFactStore, IStartupTask, IAsyncDisposable
{
    public const int RejectionsKept = 50;

    private readonly DatabaseOwner<FleetDbContext> owner = new(paths.Database("fleet"), file => new FleetDbContext(file));

    public StartupStage Stage => StartupStage.Open;

    public Task RunAsync(CancellationToken cancellationToken) => owner.OpenedAsync(cancellationToken);

    public Task<bool> IsAnnouncedAsync(string scope, Guid job, CancellationToken cancellationToken) =>
        owner.RunAsync(database => database.Announced.AnyAsync(row => row.Scope == scope && row.Job == job, cancellationToken), cancellationToken);

    public Task AnnounceAsync(string scope, Guid job, bool keyless, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                await database.Announced.AddAsync(new AnnouncedRow { Scope = scope, Job = job, Keyless = keyless }, cancellationToken);

                return await database.SaveChangesAsync(cancellationToken);
            },
            cancellationToken);

    public Task AppendAsync(string scope, Fact fact, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                await database.Outbox.AddAsync(
                    new OutboxRow
                    {
                        Scope = scope,
                        Id = fact.Id,
                        Kind = fact.Body.Kind,
                        Body = WireJson.Write(fact),
                        OccurredAt = fact.OccurredAt.UtcTicks,
                        DueAt = DateTimeOffset.MinValue.UtcTicks,
                    },
                    cancellationToken);

                return await database.SaveChangesAsync(cancellationToken);
            },
            cancellationToken);

    public Task<DateTimeOffset> LatestStampAsync(CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database => await database.Outbox.MaxAsync(row => (long?)row.OccurredAt, cancellationToken) is { } ticks
                ? new DateTimeOffset(ticks, TimeSpan.Zero)
                : DateTimeOffset.MinValue,
            cancellationToken);

    public Task<IReadOnlyList<StoredFact>> DueAsync(string scope, DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        owner.RunAsync<IReadOnlyList<StoredFact>>(
            async database =>
            [
                .. await database.Outbox.AsNoTracking()
                    .Where(row => row.Scope == scope && row.DueAt <= now.UtcTicks)
                    .OrderBy(row => row.Sequence)
                    .Take(limit)
                    .Select(row => new StoredFact(row.Id, row.Kind, row.Body))
                    .ToListAsync(cancellationToken),
            ],
            cancellationToken);

    public Task<Option<DateTimeOffset>> NextDueAsync(string scope, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database => await database.Outbox.Where(row => row.Scope == scope).MinAsync(row => (long?)row.DueAt, cancellationToken) is { } ticks
                ? Option<DateTimeOffset>.Some(new DateTimeOffset(Math.Max(ticks, DateTimeOffset.MinValue.UtcTicks), TimeSpan.Zero))
                : Option<DateTimeOffset>.None,
            cancellationToken);

    public Task SettleAsync(string scope, ReceiptPlan plan, DateTimeOffset now, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                var touched = plan.Delete.Concat(plan.Deferred.Select(fact => fact.Id)).ToList();
                var rows = await database.Outbox.Where(row => row.Scope == scope && touched.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);

                foreach (var rejected in plan.Rejected.Where(fact => rows.ContainsKey(fact.Id)))
                {
                    await database.Rejections.AddAsync(
                        new RejectionRow { Scope = scope, Kind = rows[rejected.Id].Kind, Reason = rejected.Reason, At = now.UtcTicks },
                        cancellationToken);
                }

                database.Outbox.RemoveRange(plan.Delete.Where(rows.ContainsKey).Select(id => rows[id]));

                foreach (var deferred in plan.Deferred.Where(fact => rows.ContainsKey(fact.Id)))
                {
                    var row = rows[deferred.Id];
                    row.Attempts++;
                    row.DueAt = (now + Backoff.After(row.Attempts)).UtcTicks;
                    row.DeferredReason = deferred.Reason;
                    row.FirstDeferredAt = row.FirstDeferredAt == 0 ? now.UtcTicks : row.FirstDeferredAt;
                }

                var kept = await ScopeAsync(database, scope, cancellationToken);
                kept.LastDelivery = now.UtcTicks;
                await database.SaveChangesAsync(cancellationToken);
                var newest = await database.Rejections.Where(row => row.Scope == scope).MaxAsync(row => (long?)row.Key, cancellationToken) ?? 0;

                return await database.Rejections.Where(row => row.Scope == scope && row.Key <= newest - RejectionsKept).ExecuteDeleteAsync(cancellationToken);
            },
            cancellationToken);

    public Task<int> ExpireAsync(string scope, DateTimeOffset now, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                var oldest = (now - DeferredExpiry.Lifetime).UtcTicks;
                var dropped = await database.Outbox.Where(row => row.Scope == scope && row.FirstDeferredAt != 0 && row.FirstDeferredAt < oldest).ExecuteDeleteAsync(cancellationToken);

                if (dropped > 0)
                {
                    var kept = await ScopeAsync(database, scope, cancellationToken);
                    kept.Dropped += dropped;
                    await database.SaveChangesAsync(cancellationToken);
                }

                return dropped;
            },
            cancellationToken);

    public Task<OutboxSummary> SummaryAsync(string scope, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                var waiting = await database.Outbox.CountAsync(row => row.Scope == scope, cancellationToken);
                var kept = await database.Scopes.AsNoTracking().SingleOrDefaultAsync(row => row.Scope == scope, cancellationToken);
                var rejections = await database.Rejections.AsNoTracking()
                    .Where(row => row.Scope == scope)
                    .OrderByDescending(row => row.Key)
                    .Take(RejectionsKept)
                    .ToListAsync(cancellationToken);

                return new OutboxSummary(
                    waiting,
                    kept is { LastDelivery: > 0 } ? new DateTimeOffset(kept.LastDelivery, TimeSpan.Zero) : Option<DateTimeOffset>.None,
                    [.. rejections.Select(row => new RejectionNote(row.Kind, row.Reason, new DateTimeOffset(row.At, TimeSpan.Zero)))],
                    kept?.Dropped ?? 0);
            },
            cancellationToken);

    public Task KeepOnlyAsync(string scope, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                await database.Outbox.Where(row => row.Scope != scope).ExecuteDeleteAsync(cancellationToken);
                await database.Announced.Where(row => row.Scope != scope).ExecuteDeleteAsync(cancellationToken);
                await database.Rejections.Where(row => row.Scope != scope).ExecuteDeleteAsync(cancellationToken);

                return await database.Scopes.Where(row => row.Scope != scope).ExecuteDeleteAsync(cancellationToken);
            },
            cancellationToken);

    public Task ForgetAsync(CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                await database.Outbox.ExecuteDeleteAsync(cancellationToken);
                await database.Announced.ExecuteDeleteAsync(cancellationToken);
                await database.Rejections.ExecuteDeleteAsync(cancellationToken);

                return await database.Scopes.ExecuteDeleteAsync(cancellationToken);
            },
            cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private static async Task<ScopeRow> ScopeAsync(FleetDbContext database, string scope, CancellationToken cancellationToken)
    {
        if (await database.Scopes.SingleOrDefaultAsync(row => row.Scope == scope, cancellationToken) is { } found)
        {
            return found;
        }

        var created = new ScopeRow { Scope = scope };
        await database.Scopes.AddAsync(created, cancellationToken);

        return created;
    }
}
