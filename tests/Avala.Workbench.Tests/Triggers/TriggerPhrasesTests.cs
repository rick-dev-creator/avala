using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Triggers.Contracts;
using Avala.Workbench.Triggers;

namespace Avala.Workbench.Tests.Triggers;

public sealed class TriggerPhrasesTests
{
    private static readonly TriggerId Id = new(TriggerId.Machine, "nightly");

    private static readonly DateTimeOffset At = new(2026, 10, 9, 13, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, "Every minute")]
    [InlineData(45, "Every 45 minutes")]
    [InlineData(60, "Every hour")]
    [InlineData(720, "Every 12 hours")]
    [InlineData(1440, "Every day")]
    [InlineData(2880, "Every 2 days")]
    public void AnIntervalSaysHowOftenItFires(int minutes, string phrase) =>
        Assert.Equal(phrase, TriggerPhrases.Fires(State(TriggerKind.Interval) with { EveryMinutes = minutes }));

    [Fact]
    public void AFixedTimeSaysWhichDaysAndWhen()
    {
        var at = State(TriggerKind.FixedTime) with { At = new TimeOnly(9, 5) };

        Assert.Equal(
            ["Every day at 09:05", "Weekends at 09:05", "Mon, Wed at 09:05"],
            [TriggerPhrases.Fires(at), TriggerPhrases.Fires(at with { Days = [DayOfWeek.Sunday, DayOfWeek.Saturday] }), TriggerPhrases.Fires(at with { Days = [DayOfWeek.Wednesday, DayOfWeek.Monday] })]);
    }

    [Fact]
    public void ARunSaysWhenWhatStartedItAndWhatCameOfIt()
    {
        var run = new TriggerRun(Guid.NewGuid(), Id, TriggerOrigin.Schedule, "schedule", At, RunOutcome.Submitted);

        Assert.Equal(
            [
                "2026-10-09 13:00 · on schedule · job submitted",
                "2026-10-09 13:00 · caught up · missed 3 while Avala was closed",
                "2026-10-09 13:00 · webhook · skipped, at its concurrency cap",
                "2026-10-09 13:00 · external · refused: UnknownConnection",
                "2026-10-09 13:00 · on schedule · queued in the loop",
                "2026-10-09 13:00 · on schedule · dropped at restart",
            ],
            [
                TriggerPhrases.Run(run, TimeZoneInfo.Utc),
                TriggerPhrases.Run(run with { Origin = TriggerOrigin.CatchUp, Outcome = RunOutcome.Missed, Missed = 3 }, TimeZoneInfo.Utc),
                TriggerPhrases.Run(run with { Origin = TriggerOrigin.Webhook, Outcome = RunOutcome.AtConcurrencyCap }, TimeZoneInfo.Utc),
                TriggerPhrases.Run(run with { Origin = TriggerOrigin.External, Outcome = RunOutcome.Rejected, Rejection = JobRejection.UnknownConnection }, TimeZoneInfo.Utc),
                TriggerPhrases.Run(run with { Outcome = RunOutcome.Enqueued }, TimeZoneInfo.Utc),
                TriggerPhrases.Run(run with { Outcome = RunOutcome.Dropped }, TimeZoneInfo.Utc),
            ]);
    }

    [Theory]
    [InlineData(DeliveryVerdict.Accepted, "accepted")]
    [InlineData(DeliveryVerdict.NotPost, "not a POST")]
    [InlineData(DeliveryVerdict.UnknownTrigger, "no such trigger")]
    [InlineData(DeliveryVerdict.TooLarge, "too large")]
    [InlineData(DeliveryVerdict.RateLimited, "rate limited")]
    [InlineData(DeliveryVerdict.NotSigned, "not signed")]
    [InlineData(DeliveryVerdict.NoSecret, "secret not set")]
    [InlineData(DeliveryVerdict.BadSignature, "bad signature")]
    [InlineData(DeliveryVerdict.Stale, "stale timestamp")]
    [InlineData(DeliveryVerdict.Replayed, "replayed")]
    [InlineData(DeliveryVerdict.Malformed, "not a JSON object")]
    [InlineData(DeliveryVerdict.Disabled, "trigger disabled")]
    public void ADeliverySaysWhenItCameWhereToAndItsVerdict(DeliveryVerdict verdict, string phrase) =>
        Assert.Equal(
            $"2026-10-09 13:00:00 · /hooks/issue · {phrase}",
            TriggerPhrases.Delivery(new WebhookDelivery(Guid.NewGuid(), At, "/hooks/issue", verdict), TimeZoneInfo.Utc));

    [Fact]
    public void TheEndpointSaysWhereItListensOrWhyWebhooksAreOff() =>
        Assert.Equal(
            ["Listening on http://localhost:1/hooks/<id>, on this computer only.", "Webhooks are off: no port could be leased.", "Webhooks are off: the listener could not start.", "No webhook trigger is declared."],
            [
                TriggerPhrases.Endpoint(new WebhookEndpoint("http://localhost:1/hooks/", Option<TriggerError>.None)),
                TriggerPhrases.Endpoint(new WebhookEndpoint(Option<string>.None, TriggerError.NoPortLease)),
                TriggerPhrases.Endpoint(new WebhookEndpoint(Option<string>.None, TriggerError.ListenerFailed)),
                TriggerPhrases.Endpoint(WebhookEndpoint.Off),
            ]);

    [Fact]
    public void AFileSaysWhetherItAppliesAndWhyItWasRejected() =>
        Assert.Equal(
            ["/r/.avala/triggers.json · applied, 1 trigger", "/r/.avala/triggers.json · absent", "/r/.avala/triggers.json · rejected: InvalidTime"],
            [
                TriggerPhrases.File(new TriggerFile("/r/.avala/triggers.json", TriggerFileStatus.Applied) { Triggers = 1 }),
                TriggerPhrases.File(new TriggerFile("/r/.avala/triggers.json", TriggerFileStatus.Absent)),
                TriggerPhrases.File(new TriggerFile("/r/.avala/triggers.json", TriggerFileStatus.Rejected) { Error = TriggerError.InvalidTime }),
            ]);

    private static TriggerState State(TriggerKind kind) => new(Id, "/r", kind, TriggerTarget.Job, Autonomy.Supervised, 1, Enabled: true);
}
