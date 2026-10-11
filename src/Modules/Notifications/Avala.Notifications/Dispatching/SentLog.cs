using System.Collections.Immutable;
using Avala.Notifications.Contracts;
using Avala.Notifications.Pacing;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Notifications.Dispatching;

internal sealed record SentSubject(string Subject, string Handle, DateTimeOffset At);

internal interface INotificationStore
{
    Task<IReadOnlyList<SentSubject>> SentAsync(DateTimeOffset since, CancellationToken cancellationToken);

    Task KeepSentAsync(IReadOnlyList<SentSubject> sent, CancellationToken cancellationToken);

    Task ForgetAsync(string handle, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationRecord>> RecordsAsync(CancellationToken cancellationToken);

    Task AddRecordAsync(NotificationRecord record, CancellationToken cancellationToken);
}

internal sealed class SentLog(INotificationStore store, IEventBus bus)
{
    public const int RecordsKept = 100;

    public static TimeSpan Remembered { get; } = TimeSpan.FromDays(7);

    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private ImmutableList<SentSubject> sent = [];
    private ImmutableList<NotificationRecord> records = [];
    private bool loaded;

    public IReadOnlyList<NotificationRecord> Recent => Volatile.Read(ref records);

    public ImmutableHashSet<string> Subjects => [.. sent.Select(subject => subject.Subject)];

    public bool IsCapped(DateTimeOffset now) =>
        records.Where(record => record.Outcome == NotificationOutcome.Sent && record.At > now - Hour).Select(record => record.At).Distinct().Count() >= Circumstances.MostPerHour;

    public async Task LoadedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (loaded)
        {
            return;
        }

        sent = [.. await store.SentAsync(now - Remembered, cancellationToken)];
        Volatile.Write(ref records, [.. (await store.RecordsAsync(cancellationToken)).TakeLast(RecordsKept)]);
        loaded = true;
    }

    public async Task SettleAsync(Pending entry, NotificationRecord record, CancellationToken cancellationToken)
    {
        if (record.Outcome == NotificationOutcome.Sent)
        {
            var kept = entry.Handles.Select(handle => new SentSubject(entry.Notice.Subject, handle, record.At)).ToList();
            sent = sent.AddRange(kept);
            await store.KeepSentAsync(kept, cancellationToken);
        }

        await RecordAsync(record, cancellationToken);
    }

    public async Task RecordAsync(NotificationRecord record, CancellationToken cancellationToken)
    {
        var kept = records.Add(record);
        Volatile.Write(ref records, kept.Count > RecordsKept ? kept.RemoveRange(0, kept.Count - RecordsKept) : kept);
        await store.AddRecordAsync(record, cancellationToken);
        await bus.PublishAsync(new NotificationSettled(record), cancellationToken);
    }

    public async Task ForgetAsync(string handle, CancellationToken cancellationToken)
    {
        if (sent.Exists(subject => subject.Handle == handle))
        {
            sent = sent.RemoveAll(subject => subject.Handle == handle);
            await store.ForgetAsync(handle, cancellationToken);
        }
    }
}
