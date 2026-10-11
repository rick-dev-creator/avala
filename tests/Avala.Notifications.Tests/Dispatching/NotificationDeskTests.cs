using Avala.Jobs.Contracts;
using Avala.Notifications.Contracts;
using Avala.Notifications.Pacing;

namespace Avala.Notifications.Tests.Dispatching;

public sealed class NotificationDeskTests
{
    [Fact]
    public async Task ANoticeIsTitledByItsJobAndRepositoryFromTheCatalogAsync()
    {
        await using var notified = new Notified();

        await notified.Desk.HandleAsync(Samples.Asked("tool-1"), Notified.Cancellation);
        await notified.AdvanceAsync(Outbox.Coalescing);

        var message = Assert.Single(notified.Channel.Sent);
        Assert.Equal(("A decision needs you · ledger-api", "Fix the flaky login test"), (message.Title, message.Body));
    }

    [Fact]
    public async Task AChildJobsFailureIsLeftToItsParentButItsDecisionReachesThePersonAsync()
    {
        await using var notified = new Notified();
        notified.Catalog.With(Samples.Job, Samples.Other);

        await notified.Desk.HandleAsync(new JobProgressed(Samples.Job, JobStatus.Failed), Notified.Cancellation);
        await notified.Desk.HandleAsync(Samples.Asked("tool-1"), Notified.Cancellation);
        await notified.AdvanceAsync(Outbox.Coalescing);

        Assert.Equal(NotificationKind.Decision, Assert.Single(notified.Channel.Sent).Kind);
    }

    [Fact]
    public async Task AnAnswerInTheAppWithdrawsTheDecisionBeforeItIsSentAsync()
    {
        await using var notified = new Notified();

        await notified.Desk.HandleAsync(Samples.Asked("tool-1"), Notified.Cancellation);
        await notified.Desk.HandleAsync(Samples.Answered("tool-1"), Notified.Cancellation);
        await notified.AdvanceAsync(Outbox.Coalescing);

        Assert.Empty(notified.Channel.Sent);
        Assert.Equal(NotificationOutcome.Withdrawn, Assert.Single(notified.Records).Outcome);
    }
}
