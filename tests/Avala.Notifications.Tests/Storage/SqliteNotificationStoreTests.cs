using Avala.Notifications.Contracts;
using Avala.Notifications.Dispatching;
using Avala.Notifications.Storage;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Notifications.Tests.Storage;

public sealed class SqliteNotificationStoreTests
{
    [Fact]
    public async Task SentSubjectsAndRecordsSurviveAReopeningUntilForgottenOrTooOldAsync()
    {
        await using var folder = new TemporaryFolder();
        var old = new SentSubject("job:a:failed", "job:a:failed", Samples.Monday.AddDays(-8));
        var kept = new SentSubject("job:b:decision", "item:b:1", Samples.Monday);
        var forgotten = new SentSubject("job:b:decision", "item:b:2", Samples.Monday);
        var record = new NotificationRecord(Guid.NewGuid(), NotificationKind.Decision, Severity.Urgent, Samples.Monday, NotificationOutcome.Sent) { Job = Samples.Job, Repository = "ledger-api", Attempts = 2 };

        await using (var first = new SqliteNotificationStore(new AvalaPaths(folder.Path)))
        {
            await first.RunAsync(Notified.Cancellation);
            await first.KeepSentAsync([old, kept, forgotten], Notified.Cancellation);
            await first.ForgetAsync("item:b:2", Notified.Cancellation);
            await first.AddRecordAsync(record, Notified.Cancellation);
        }

        await using var second = new SqliteNotificationStore(new AvalaPaths(folder.Path));

        Assert.Equal([kept], await second.SentAsync(Samples.Monday - SentLog.Remembered, Notified.Cancellation));
        Assert.Equal([record], await second.RecordsAsync(Notified.Cancellation));
    }
}
