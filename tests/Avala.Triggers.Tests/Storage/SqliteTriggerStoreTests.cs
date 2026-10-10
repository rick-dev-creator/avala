using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Triggers.Contracts;
using Avala.Triggers.Scheduling;
using Avala.Triggers.Storage;

namespace Avala.Triggers.Tests.Storage;

public sealed class SqliteTriggerStoreTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private static readonly TriggerId Issue = new(TriggerId.Machine, "issue");

    [Fact]
    public async Task SchedulesRunsAndDeliveriesSurviveAReopeningAsTheirLatestVersionsAsync()
    {
        await using var folder = new TemporaryFolder();
        var schedule = new ScheduleState("machine:nightly", Nine) { Anchor = Nine.AddHours(1), Enabled = true };
        var run = new TriggerRun(Guid.NewGuid(), Issue, TriggerOrigin.Webhook, "webhook", Nine, RunOutcome.Submitted) { PayloadDigest = "abc" };
        var delivery = new WebhookDelivery(Guid.NewGuid(), Nine, "issue", DeliveryVerdict.Accepted) { Trigger = Issue, Nonce = "n-1", Run = run.Id };
        await using (var first = new SqliteTriggerStore(new AvalaPaths(folder.Path)))
        {
            await first.RunAsync(Triggered.Cancellation);
            await first.KeepAsync(schedule with { Enabled = Option<bool>.None }, Triggered.Cancellation);
            await first.KeepAsync(schedule, Triggered.Cancellation);
            await first.KeepRunAsync(run with { Outcome = RunOutcome.Rejected }, Triggered.Cancellation);
            await first.KeepRunAsync(run, Triggered.Cancellation);
            await first.AddDeliveryAsync(delivery, Triggered.Cancellation);
        }

        await using var second = new SqliteTriggerStore(new AvalaPaths(folder.Path));
        await second.RunAsync(Triggered.Cancellation);

        Assert.Equal([schedule], await second.SchedulesAsync(Triggered.Cancellation));
        Assert.Equal([run], await second.RunsAsync(Triggered.Cancellation));
        Assert.Equal([delivery], await second.DeliveriesAsync(Triggered.Cancellation));
    }
}
