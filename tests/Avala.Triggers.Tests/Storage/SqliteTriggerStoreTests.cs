using Avala.Agents.Contracts.Connections;
using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Triggers.Contracts;
using Avala.Triggers.Looping;
using Avala.Triggers.Scheduling;
using Avala.Triggers.Storage;

namespace Avala.Triggers.Tests.Storage;

public sealed class SqliteTriggerStoreTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private static readonly TriggerId Issue = new(TriggerId.Machine, "issue");

    [Fact]
    public async Task SchedulesRunsDeliveriesAndEventKeysSurviveAReopeningAsTheirLatestVersionsAsync()
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
            await first.KeepEventKeyAsync("machine#issue|forge:github:issue-opened:12", Triggered.Cancellation);
            await first.KeepEventKeyAsync("machine#issue|forge:github:issue-opened:12", Triggered.Cancellation);
        }

        await using var second = new SqliteTriggerStore(new AvalaPaths(folder.Path));
        await second.RunAsync(Triggered.Cancellation);

        Assert.Equal([schedule], await second.SchedulesAsync(Triggered.Cancellation));
        Assert.Equal([run], await second.RunsAsync(Triggered.Cancellation));
        Assert.Equal([delivery], await second.DeliveriesAsync(Triggered.Cancellation));
        Assert.Equal(["machine#issue|forge:github:issue-opened:12"], await second.EventKeysAsync(Triggered.Cancellation));
    }

    [Fact]
    public async Task ALoopTaskQueuedAndNotTakenSurvivesAReopeningAndATakenOneDoesNotAsync()
    {
        await using var folder = new TemporaryFolder();
        var waiting = Queued(Guid.NewGuid(), "Update the dependencies");
        var taken = Queued(Guid.NewGuid(), "Fix the docs");
        await using (var first = new SqliteTriggerStore(new AvalaPaths(folder.Path)))
        {
            await first.QueueAsync(waiting, Triggered.Cancellation);
            await first.QueueAsync(taken, Triggered.Cancellation);
            await first.TakeAsync(taken.Run, Triggered.Cancellation);
        }

        await using var second = new SqliteTriggerStore(new AvalaPaths(folder.Path));

        Assert.Equal([waiting], await second.QueuedAsync(Triggered.Cancellation));
    }

    private static QueuedTask Queued(Guid run, string instruction) =>
        new(run, Issue, new SourcedTask("trigger", run.ToString("N"), Triggered.Repository, instruction))
        {
            Connection = new ConnectionName("work"),
            Autonomy = Autonomy.Autonomous,
            Attempts = 2,
        };
}
