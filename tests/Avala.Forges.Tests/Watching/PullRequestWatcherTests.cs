using Avala.Forges.Contracts;
using Avala.Forges.Watching;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Forges.Tests.Watching;

public sealed class PullRequestWatcherTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AFailedReadBacksOffWithItsErrorAndPollsAgainOnceTheBackoffPassesAsync()
    {
        await using var world = await new WatcherWorld().WatchingAsync(watch => watch);
        world.Forge.Observed = ForgeError.Unreachable;

        var backingOff = await world.RefreshAsync();
        world.Forge.Observed = WatcherWorld.Observing(CheckStatus.Pending);
        world.Clock.Advance(WatcherWorld.Poll * 2);
        var recovered = await world.ChangedAsync(state => state.Status == WatchStatus.Watching);

        Assert.Equal(
            (WatchStatus.BackingOff, Option<ForgeError>.Some(ForgeError.Unreachable), 1, Option<DateTimeOffset>.Some(world.Start + (WatcherWorld.Poll * 2))),
            (backingOff.Status, backingOff.Failure, backingOff.Failures, backingOff.NextPoll));
        Assert.Equal((Option<ForgeError>.None, 0, 2), (recovered.Failure, recovered.Failures, world.Forge.Reads));
    }

    [Theory]
    [InlineData(PullRequestLifecycle.Merged, true, WatchEnd.Merged)]
    [InlineData(PullRequestLifecycle.Closed, true, WatchEnd.Closed)]
    [InlineData(PullRequestLifecycle.Open, false, WatchEnd.JobEnded)]
    public async Task AMergedOrClosedPullRequestOrAJobThatIsGoneEndsTheWatchAsync(PullRequestLifecycle lifecycle, bool jobKnown, WatchEnd end)
    {
        await using var world = await new WatcherWorld().WatchingAsync(watch => watch);
        world.Forge.Observed = Outcomes.Succeeds(WatcherWorld.Observing(CheckStatus.Passed)) with { Lifecycle = lifecycle };
        world.Catalog.Status = jobKnown ? JobStatus.Approved : Option<JobStatus>.None;

        var ended = await world.RefreshAsync();

        Assert.Equal((WatchStatus.Ended, Option<WatchEnd>.Some(end), Option<DateTimeOffset>.None), (ended.Status, ended.Ended, ended.NextPoll));
    }

    [Fact]
    public async Task FailedChecksWakeTheAgentWithTheirFeedbackRecordTheWakeUpAndWaitForTheJobAsync()
    {
        await using var world = await new WatcherWorld().WatchingAsync(watch => watch);
        world.Forge.Observed = WatcherWorld.Observing(CheckStatus.Failed);
        world.Deliveries.Continues(world.Job);

        var woken = await world.RefreshAsync();
        var wakeUp = Assert.Single(world.Book.WakeUpsOf(world.Job));

        Assert.Equal(
            (WatchStatus.WaitingForJob, 1, Option<WakeReason>.Some(WakeReason.ChecksFailed), Option<DateTimeOffset>.Some(world.Start + WatcherWorld.Poll)),
            (woken.Status, woken.WakeUps, woken.Pending, woken.NextPoll));
        Assert.Equal(
            (WakeOutcome.Woken, $"checks:{WatcherWorld.Head}", Assert.Single(world.Deliveries.Feedback), Option<ContinuedIn>.Some(ContinuedIn.ResumedConversation)),
            (wakeUp.Outcome, wakeUp.Key, wakeUp.Feedback, wakeUp.Conversation));
        Assert.Contains("1 test failed", wakeUp.Feedback, StringComparison.Ordinal);
        Assert.Equal([wakeUp.Key], Outcomes.Present(world.Book.Find(world.Job)).Handled);
    }

    [Fact]
    public async Task AWakeUpTheJobRefusesIsRecordedWithItsRefusalAndTheWatchGoesOnAsync()
    {
        await using var world = await new WatcherWorld().WatchingAsync(watch => watch);
        world.Forge.Observed = WatcherWorld.Observing(CheckStatus.Failed);
        world.Deliveries.Reopening = JobRejection.NotAwaitingReview;

        var refused = await world.RefreshAsync();
        var wakeUp = Assert.Single(world.Book.WakeUpsOf(world.Job));

        Assert.Equal((WatchStatus.Watching, 1), (refused.Status, refused.WakeUps));
        Assert.Equal(
            (WakeOutcome.Refused, Option<JobRejection>.Some(JobRejection.NotAwaitingReview), Option<ContinuedIn>.None),
            (wakeUp.Outcome, wakeUp.Refusal, wakeUp.Conversation));
    }

    [Fact]
    public async Task AConflictFetchesTheBaseIntoTheJobsWorktreeBeforeWakingTheAgentAsync()
    {
        await using var world = await new WatcherWorld().WatchingAsync(watch => watch);
        world.Forge.Observed = WatcherWorld.Observing(CheckStatus.Passed, Mergeability.Conflicting);
        world.Deliveries.Continues(world.Job);

        _ = await world.RefreshAsync();

        Assert.Equal([(WatcherWorld.Worktree, "origin", "main")], world.Git.Fetched);
        Assert.Equal(WakeReason.Conflict, Assert.Single(world.Book.WakeUpsOf(world.Job)).Reason);
    }

    [Fact]
    public async Task WhenTheWakeUpsAreSpentTheWatchHoldsThePullRequestForAPersonAndSaysSoOnItAsync()
    {
        await using var world = await new WatcherWorld().WatchingAsync(watch => watch with { WakeUps = 3 });
        world.Forge.Observed = WatcherWorld.Observing(CheckStatus.Failed);

        var held = await world.RefreshAsync();
        var wakeUp = Assert.Single(world.Book.WakeUpsOf(world.Job));

        Assert.Equal((WatchStatus.NeedsPerson, 3, Option<DateTimeOffset>.Some(world.Start + WatcherWorld.Poll)), (held.Status, held.WakeUps, held.NextPoll));
        Assert.Equal((WakeOutcome.HeldForPerson, Assert.Single(world.Forge.Comments)), (wakeUp.Outcome, wakeUp.Feedback));
        Assert.Empty(world.Deliveries.Feedback);
    }

    [Theory]
    [InlineData(JobStatus.Running, WatchStatus.BackingOff, WatchStatus.WaitingForJob)]
    [InlineData(JobStatus.Approved, WatchStatus.BackingOff, WatchStatus.Watching)]
    [InlineData(JobStatus.Approved, WatchStatus.NeedsPerson, WatchStatus.NeedsPerson)]
    public async Task APollThatWakesNobodyClearsTheFailuresAndPollsAgainAfterTheIntervalAsync(JobStatus job, WatchStatus before, WatchStatus after)
    {
        await using var world = await new WatcherWorld().WatchingAsync(watch => watch with { Status = before, Failure = ForgeError.Unreachable, Failures = 2 });
        world.Catalog.Status = job;

        var polled = await world.RefreshAsync();

        Assert.Equal(
            (after, Option<ForgeError>.None, 0, Option<DateTimeOffset>.Some(world.Start), Option<DateTimeOffset>.Some(world.Start + WatcherWorld.Poll), true),
            (polled.Status, polled.Failure, polled.Failures, polled.LastPolled, polled.NextPoll, polled.Last.IsSome));
    }

    [Fact]
    public async Task RefreshingAJobWithoutAWatchIsNotFoundAsync()
    {
        await using var world = new WatcherWorld();

        Assert.Equal(ForgeError.NotFound, Outcomes.FailsWith(await world.Watcher.RefreshAsync(world.Job, Cancellation)));
    }

    [Fact]
    public async Task RefreshingAnEndedWatchShowsItWithoutAskingTheForgeAsync()
    {
        await using var world = await new WatcherWorld().WatchingAsync(watch => watch with { Status = WatchStatus.Ended, Ended = WatchEnd.Merged });

        var ended = await world.RefreshAsync();

        Assert.Equal((WatchStatus.Ended, 0), (ended.Status, world.Forge.Reads));
    }

    [Theory]
    [InlineData(JobStatus.Discarded)]
    [InlineData(JobStatus.Failed)]
    public async Task AJobThatIsDiscardedOrFailsEndsItsWatchAsync(JobStatus status)
    {
        await using var world = await new WatcherWorld().WatchingAsync(watch => watch);
        _ = await world.RefreshAsync();

        await world.Watcher.HandleAsync(new JobProgressed(world.Job, status), Cancellation);
        var ended = await world.ChangedAsync(state => state.Status == WatchStatus.Ended);

        Assert.Equal((Option<WatchEnd>.Some(WatchEnd.JobEnded), Option<DateTimeOffset>.None), (ended.Ended, ended.NextPoll));
    }

    [Theory]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Checking)]
    [InlineData(JobStatus.NeedsHelp)]
    [InlineData(JobStatus.AwaitingReview)]
    public async Task AJobBackAtWorkMakesItsWatchWaitForItAsync(JobStatus status)
    {
        await using var world = await new WatcherWorld().WatchingAsync(watch => watch);

        await world.Watcher.HandleAsync(new JobProgressed(world.Job, status), Cancellation);

        Assert.Equal(WatchStatus.WaitingForJob, (await world.ChangedAsync(_ => true)).Status);
    }

    [Fact]
    public async Task AWokenFixAwaitingReviewIsRedeliveredToThePullRequestUnderAutomaticRedeliveryAsync()
    {
        await using var world = await new WatcherWorld().WatchingAsync(watch => watch with { Status = WatchStatus.WaitingForJob, WakeUps = 1, Redelivery = Redelivery.Automatic });

        await world.Watcher.HandleAsync(new JobProgressed(world.Job, JobStatus.AwaitingReview), Cancellation);

        Assert.Equal(PullRequestDelivery.Strategy, await world.Deliveries.Redelivered.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation));
    }

    [Fact]
    public async Task UnderReviewRedeliveryAWokenFixAwaitingReviewWaitsForAPersonAsync()
    {
        await using var world = await new WatcherWorld().WatchingAsync(watch => watch with { WakeUps = 1, Redelivery = Redelivery.Review });

        await world.Watcher.HandleAsync(new JobProgressed(world.Job, JobStatus.AwaitingReview), Cancellation);

        Assert.Equal(WatchStatus.WaitingForJob, (await world.ChangedAsync(_ => true)).Status);
        Assert.False(world.Deliveries.Redelivered.Task.IsCompleted);
    }

    [Fact]
    public async Task TheProgressOfAJobWhoseWatchEndedChangesNothingAsync()
    {
        await using var world = await new WatcherWorld().WatchingAsync(watch => watch with { Status = WatchStatus.Ended, Ended = WatchEnd.Merged });

        await world.Watcher.HandleAsync(new JobProgressed(world.Job, JobStatus.Failed), Cancellation);
        await world.SettledAsync();

        Assert.Empty(world.Bus.Published);
    }

    [Fact]
    public async Task AtStartupEveryLiveWatchIsPolledAndAnEndedOneIsNotAsync()
    {
        await using var world = new WatcherWorld();
        world.Store.Kept.Add(new KeptWatch(WatcherWorld.Watch(new JobId(Guid.CreateVersion7())) with { Status = WatchStatus.Ended }, "origin", "https://forge.example/octo/shop.git", []));
        _ = await world.WatchingAsync(watch => watch);

        await world.Watcher.RunAsync(Cancellation);
        var polled = await world.ChangedAsync(state => state.LastPolled.IsSome);
        await world.SettledAsync();

        Assert.Equal((Option<DateTimeOffset>.Some(world.Start), 1), (polled.LastPolled, world.Forge.Reads));
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(-30, 0)]
    public async Task AnOpenedPullRequestIsFirstPolledWhenItsNextPollIsDueAsync(int nextPoll, int wait)
    {
        await using var world = new WatcherWorld();
        _ = await world.WatchingAsync(watch => watch with { NextPoll = world.Start.AddSeconds(nextPoll) });

        await world.Watcher.HandleAsync(new PullRequestOpened(world.Job, WatcherWorld.Watch(world.Job).Forge, WatcherWorld.Pull), Cancellation);
        await world.SettledAsync();
        world.Clock.Advance(TimeSpan.FromSeconds(wait));
        var polled = await world.ChangedAsync(state => state.LastPolled.IsSome);

        Assert.Equal(world.Start.AddSeconds(wait), Outcomes.Present(polled.LastPolled));
    }
}
