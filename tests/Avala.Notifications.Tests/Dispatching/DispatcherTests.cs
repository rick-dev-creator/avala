using Avala.Notifications.Contracts;
using Avala.Notifications.Delivering;
using Avala.Notifications.Pacing;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Notifications.Tests.Dispatching;

public sealed class DispatcherTests
{
    private static readonly TimeSpan Window = Outbox.Coalescing;

    [Fact]
    public async Task ANoticeWaitsThirtySecondsThenReachesAnAbsentPersonOnceAsync()
    {
        await using var notified = new Notified();
        await notified.OfferAsync(Samples.Decision(Samples.Job));

        await notified.AdvanceAsync(Window - TimeSpan.FromSeconds(1));
        var early = notified.Channel.Sent.Count;
        await notified.AdvanceAsync(TimeSpan.FromSeconds(1));

        var sent = await notified.SettledAsync(NotificationOutcome.Sent);
        Assert.Equal(0, early);
        Assert.Equal("A decision needs you · ledger-api", Assert.Single(notified.Channel.Sent).Title);
        Assert.Equal((NotificationKind.Decision, Samples.Job, 1), (sent.Kind, Outcomes.Present(sent.Job), sent.Attempts));
    }

    [Fact]
    public async Task NoticesWithinTheWindowAreSentAsOneSummaryAsync()
    {
        await using var notified = new Notified();
        await notified.OfferAsync(Samples.Decision(Samples.Job));
        await notified.AdvanceAsync(TimeSpan.FromSeconds(20));
        await notified.OfferAsync(Samples.Failed(Samples.Other));

        await notified.AdvanceAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((NotificationKind.Summary, "2 things need you"), Assert.Single(notified.Channel.Sent) is var message ? (message.Kind, message.Title) : default);
        Assert.Equal([NotificationOutcome.Sent, NotificationOutcome.Sent], notified.Records.Select(record => record.Outcome));
    }

    [Fact]
    public async Task AnUrgentNoticeWaitsWhileThePersonIsAtTheWindowAndAnInfoNoticeIsDroppedAsync()
    {
        await using var notified = new Notified();
        notified.Presence.SeenNow();
        await notified.OfferAsync(Samples.Decision(Samples.Job));
        await notified.OfferAsync(Samples.Failed(Samples.Other));

        await notified.AdvanceAsync(Window);
        var present = notified.Channel.Sent.Count;
        await notified.AdvanceAsync(Circumstances.AwayAfter - Window);

        Assert.Equal(0, present);
        Assert.Equal(NotificationKind.Decision, Assert.Single(notified.Channel.Sent).Kind);
        Assert.Equal([(NotificationKind.Failed, NotificationOutcome.Present), (NotificationKind.Decision, NotificationOutcome.Sent)], notified.Records.Select(record => (record.Kind, record.Outcome)));
    }

    [Fact]
    public async Task ASubjectAlreadySentIsNotSentAgainUntilItIsResolvedEvenAfterARestartAsync()
    {
        var store = new MemoryStore();
        var decision = Samples.Decision(Samples.Job);

        await using (var first = new Notified(Notified.Ntfy, store))
        {
            await first.OfferAsync(decision);
            await first.AdvanceAsync(Window);
        }

        await using var restarted = new Notified(Notified.Ntfy, store);
        await restarted.OfferAsync(decision with { Handle = "item:second" });
        await restarted.AdvanceAsync(Window);
        await restarted.Dispatcher.ResolveAsync([decision.Handle], Notified.Cancellation);
        await restarted.OfferAsync(decision with { Handle = "item:third" });
        await restarted.AdvanceAsync(Window);

        Assert.Equal([NotificationOutcome.Duplicate, NotificationOutcome.Sent], restarted.Records.Select(record => record.Outcome));
    }

    [Fact]
    public async Task QuietHoursSendUrgentNoticesAndHoldTheRestForOneDigestAtTheirEndAsync()
    {
        await using var notified = new Notified(Notified.Ntfy with { Quiet = new QuietHours(new TimeOnly(8, 0), new TimeOnly(10, 0)) });
        await notified.OfferAsync(Samples.Decision(Samples.Job));
        await notified.OfferAsync(Samples.Failed(Samples.Other));
        await notified.OfferAsync(Samples.Failed(Samples.Job));

        await notified.AdvanceAsync(Window);
        var urgent = notified.Channel.Sent.ToList();
        await notified.AdvanceAsync(TimeSpan.FromHours(1));

        Assert.Equal([NotificationKind.Decision], urgent.Select(message => message.Kind));
        Assert.Equal((NotificationKind.Digest, "While quiet: 2 updates"), notified.Channel.Sent.Last() is var digest ? (digest.Kind, digest.Title) : default);
        Assert.Equal(2, notified.Channel.Sent.Count);
    }

    [Fact]
    public async Task AfterTwentyMessagesInAnHourTheNextIsCappedAsync()
    {
        await using var notified = new Notified();

        foreach (var index in Enumerable.Range(0, Circumstances.MostPerHour + 1))
        {
            await notified.OfferAsync(Samples.Failed(new Avala.Jobs.Contracts.JobId(Guid.NewGuid())));
            await notified.AdvanceAsync(Window);
        }

        Assert.Equal(Circumstances.MostPerHour, notified.Channel.Sent.Count);
        Assert.Equal(NotificationOutcome.Capped, notified.Records[^1].Outcome);
    }

    [Fact]
    public async Task AnUnreachableChannelIsRetriedThreeTimesWithBackoffThenFailsAsync()
    {
        await using var notified = new Notified();
        notified.Channel.Failing(NotificationError.Unreachable, NotificationError.Unreachable, NotificationError.Unreachable, NotificationError.Unreachable);
        await notified.OfferAsync(Samples.Decision(Samples.Job));
        await notified.AdvanceAsync(Window);

        foreach (var wait in Courier.Backoff)
        {
            await notified.AdvanceAsync(wait - TimeSpan.FromTicks(1));
            await notified.AdvanceAsync(TimeSpan.FromTicks(1));
        }

        var failed = await notified.SettledAsync(NotificationOutcome.Failed);
        Assert.Equal((4, NotificationError.Unreachable), (failed.Attempts, Outcomes.Present(failed.Error)));
        Assert.Equal(4, notified.Channel.Sent.Count);
    }

    [Fact]
    public async Task ARefusedMessageFailsAtOnceAsync()
    {
        await using var notified = new Notified();
        notified.Channel.Failing(NotificationError.Refused);
        await notified.OfferAsync(Samples.Decision(Samples.Job));

        await notified.AdvanceAsync(Window);

        var failed = await notified.SettledAsync(NotificationOutcome.Failed);
        Assert.Equal((1, NotificationError.Refused), (failed.Attempts, Outcomes.Present(failed.Error)));
    }

    [Fact]
    public async Task NothingIsOfferedForAnEventNotChosenOrWithNotificationsTurnedOffAsync()
    {
        await using var notified = new Notified(Notified.Ntfy with { Events = [NotificationKind.Decision] });
        await notified.OfferAsync(Samples.Failed(Samples.Job));
        notified.File.With(Notified.Ntfy with { Enabled = false });
        await notified.OfferAsync(Samples.Decision(Samples.Job));

        await notified.AdvanceAsync(Window);

        Assert.Empty(notified.Channel.Sent);
        Assert.Empty(notified.Records);
    }

    [Fact]
    public async Task ATestMessageIsSentAtOnceAndRecordedAsync()
    {
        await using var notified = new Notified();

        var record = Outcomes.Succeeds(await notified.Dispatcher.TestAsync(Notified.Cancellation));

        Assert.Equal((NotificationKind.Test, NotificationOutcome.Sent), (record.Kind, record.Outcome));
        Assert.Equal("Avala test notification", Assert.Single(notified.Channel.Sent).Title);
        Assert.Equal([record], notified.Dispatcher.Recent);
    }
}
