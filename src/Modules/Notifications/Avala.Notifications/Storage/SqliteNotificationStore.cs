using Avala.Notifications.Contracts;
using Avala.Notifications.Dispatching;
using Avala.Sdk;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Notifications.Storage;

internal sealed class SqliteNotificationStore(AvalaPaths paths) : INotificationStore, IStartupTask, IAsyncDisposable
{
    private readonly DatabaseOwner<NotificationsDbContext> owner = new(paths.Database("notifications"), file => new NotificationsDbContext(file));

    public StartupStage Stage => StartupStage.Open;

    public Task RunAsync(CancellationToken cancellationToken) => owner.OpenedAsync(cancellationToken);

    public Task<IReadOnlyList<SentSubject>> SentAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        owner.RunAsync<IReadOnlyList<SentSubject>>(
            async database =>
            {
                await database.Sent.Where(row => row.At < since.UtcTicks).ExecuteDeleteAsync(cancellationToken);

                return [.. (await database.Sent.AsNoTracking().OrderBy(row => row.Key).ToListAsync(cancellationToken)).Select(row => row.Read())];
            },
            cancellationToken);

    public Task KeepSentAsync(IReadOnlyList<SentSubject> sent, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                await database.Sent.AddRangeAsync(sent.Select(StoredSent.Of), cancellationToken);

                return await SaveAsync(database, cancellationToken);
            },
            cancellationToken);

    public Task ForgetAsync(string handle, CancellationToken cancellationToken) =>
        owner.RunAsync(database => database.Sent.Where(row => row.Handle == handle).ExecuteDeleteAsync(cancellationToken), cancellationToken);

    public Task<IReadOnlyList<NotificationRecord>> RecordsAsync(CancellationToken cancellationToken) =>
        owner.RunAsync<IReadOnlyList<NotificationRecord>>(
            async database => [.. (await database.Records.AsNoTracking().OrderBy(row => row.Key).ToListAsync(cancellationToken)).Select(row => row.Read())],
            cancellationToken);

    public Task AddRecordAsync(NotificationRecord record, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                await database.Records.AddAsync(StoredRecord.Of(record), cancellationToken);
                var saved = await SaveAsync(database, cancellationToken);
                var newest = await database.Records.MaxAsync(row => (int?)row.Key, cancellationToken) ?? 0;
                await database.Records.Where(row => row.Key <= newest - SentLog.RecordsKept).ExecuteDeleteAsync(cancellationToken);

                return saved;
            },
            cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private static async Task<int> SaveAsync(NotificationsDbContext database, CancellationToken cancellationToken)
    {
        var saved = await database.SaveChangesAsync(cancellationToken);
        database.ChangeTracker.Clear();

        return saved;
    }
}
