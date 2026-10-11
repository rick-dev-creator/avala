using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Forges.Sifting;
using Avala.Sdk;
using Avala.Testing;
using Avala.Triggers.Contracts;

namespace Avala.Forges.Tests.Polling;

public sealed class ForgeEventPollerTests
{
    [Fact]
    public async Task TheFirstPollRecordsABaselineAndFiresNothingThatHappenedBeforeItAsync()
    {
        await using var world = await new PollerWorld().StartedAsync();
        world.Forge.Happened.Add(PollerWorld.LabeledBy("alice", 7001));

        var watch = await world.CheckAsync();

        Assert.Empty(world.Triggers.Fired);
        Assert.Empty(world.Log.Events());
        Assert.Equal(Option<string>.None, Assert.Single(world.Forge.Queries).Cursor);
        Assert.Equal("7001", Assert.Single(world.Store.Cursors).Position);
        Assert.Equal(
            (EventWatchStatus.Watching, "Event forge", Option<DateTimeOffset>.Some(world.Clock.GetUtcNow()), Option<DateTimeOffset>.Some(world.Clock.GetUtcNow() + PollerWorld.Poll)),
            (watch.Status, watch.Forge, watch.Baseline, watch.NextPoll));
    }

    [Fact]
    public async Task EventsSinceTheCursorAreJudgedInOrderOnlyThoseThatPassFireAndTheCursorAdvancesPastEachAsync()
    {
        await using var world = await new PollerWorld().StartedAsync();
        _ = await world.CheckAsync();
        world.Forge.Happened.AddRange([PollerWorld.LabeledBy("alice", 7002, "bug"), PollerWorld.LabeledBy("mallory", 7003), PollerWorld.LabeledBy("alice", 7004)]);

        world.Clock.Advance(PollerWorld.Poll);
        await world.SettledAsync();

        var fired = Assert.Single(world.Triggers.Fired);
        Assert.Equal((TriggerOrigin.External, "forge work · alice", Option<string>.Some("forge:work:issue-labeled:7004")), (fired.Origin, fired.Who, fired.Key));
        using var payload = JsonDocument.Parse(Outcomes.Present(fired.Payload));
        Assert.Equal(("Checkout button does nothing", "avala", 12), (payload.RootElement.GetProperty("title").GetString(), payload.RootElement.GetProperty("label").GetString(), payload.RootElement.GetProperty("number").GetInt32()));
        Assert.Equal(
            [("7002", ForgeEventVerdict.Filtered, Option<FilterReason>.Some(FilterReason.OtherLabel)), ("7003", ForgeEventVerdict.NotAllowed, Option<FilterReason>.None), ("7004", ForgeEventVerdict.Fired, Option<FilterReason>.None)],
            world.Log.Events().Select(entry => (entry.Id, entry.Verdict, entry.Reason)));
        Assert.Equal(Option<RunOutcome>.Some(RunOutcome.Submitted), world.Log.Events()[^1].Outcome);
        Assert.Equal("7004", Assert.Single(world.Store.Cursors).Position);
        Assert.Equal(Option<string>.Some("0"), world.Forge.Queries[^1].Cursor);
    }

    [Fact]
    public async Task AnEventTheTriggerAlreadyFiredIsLoggedAsADuplicateWhenTheForgeListsItAgainAsync()
    {
        await using var world = await new PollerWorld().StartedAsync();
        _ = await world.CheckAsync();
        world.Forge.Happened.Add(PollerWorld.LabeledBy("alice", 7002));
        _ = await world.CheckAsync();

        world.Forge.Happened.Add(PollerWorld.LabeledBy("alice", 7002) with { Cursor = "7003" });
        _ = await world.CheckAsync();

        Assert.Equal([ForgeEventVerdict.Fired, ForgeEventVerdict.Duplicate], world.Log.Events().Select(entry => entry.Verdict));
        Assert.Equal("7003", Assert.Single(world.Store.Cursors).Position);
    }

    [Fact]
    public async Task AFailedReadBacksOffWithItsErrorKeepsTheCursorAndRecoversOnTheNextPollAsync()
    {
        await using var world = await new PollerWorld().StartedAsync();
        _ = await world.CheckAsync();
        world.Forge.Failure = ForgeError.RateLimited;
        world.Clock.Advance(PollerWorld.Poll);
        await world.SettledAsync();
        var backingOff = Outcomes.Present(world.Book.WatchOf(PollerWorld.Labeled));

        world.Forge.Failure = Option<ForgeError>.None;
        world.Forge.Happened.Add(PollerWorld.LabeledBy("alice", 7002));
        world.Clock.Advance(PollerWorld.Poll * 2);
        await world.SettledAsync();

        Assert.Equal(
            (EventWatchStatus.BackingOff, Option<ForgeError>.Some(ForgeError.RateLimited), 1, Option<DateTimeOffset>.Some(world.Clock.GetUtcNow())),
            (backingOff.Status, backingOff.Failure, backingOff.Failures, backingOff.NextPoll));
        Assert.Single(world.Triggers.Fired);
        Assert.Equal((EventWatchStatus.Watching, 0), (Outcomes.Present(world.Book.WatchOf(PollerWorld.Labeled)).Status, Outcomes.Present(world.Book.WatchOf(PollerWorld.Labeled)).Failures));
    }

    [Fact]
    public async Task AConnectionWhoseForgeReadsNoEventsIsEventsUnsupportedAsync()
    {
        await using var world = await new PollerWorld().StartedAsync();
        world.Triggers.Declared[0] = world.Triggers.Declared[0] with { Forge = PollerWorld.Rule with { Connection = "plain" } };

        var watch = await world.CheckAsync();

        Assert.Equal((EventWatchStatus.BackingOff, Option<ForgeError>.Some(ForgeError.EventsUnsupported)), (watch.Status, watch.Failure));
    }

    [Fact]
    public async Task ADisabledTriggerIsNotPolledAndARemovedOneIsForgottenAsync()
    {
        await using var world = await new PollerWorld().StartedAsync();
        world.Triggers.Declared[0] = world.Triggers.Declared[0] with { Enabled = false };

        var disabled = await world.CheckAsync();
        world.Triggers.Declared.Clear();
        await world.Poller.HandleAsync(new TriggersChanged(world.Clock.GetUtcNow()), TestContext.Current.CancellationToken);
        await world.SettledAsync();

        Assert.Equal(EventWatchStatus.Disabled, disabled.Status);
        Assert.Empty(world.Forge.Queries);
        Assert.Empty(world.Log.Watches());
    }

    [Fact]
    public async Task AtMostTwentyEventsAreHandledInOnePollAndTheRestWaitForTheNextAsync()
    {
        await using var world = await new PollerWorld().StartedAsync();
        _ = await world.CheckAsync();
        world.Forge.Happened.AddRange(Enumerable.Range(1, 25).Select(index => PollerWorld.LabeledBy("alice", 7000 + index)));

        _ = await world.CheckAsync();

        Assert.Equal(20, world.Triggers.Fired.Count);
        Assert.Equal("7020", Assert.Single(world.Store.Cursors).Position);
    }

    [Fact]
    public async Task ANudgeVerifiedByTheConnectionsForgePollsItsTriggerAtOnceAsync()
    {
        await using var world = await new PollerWorld().StartedAsync();
        _ = await world.CheckAsync();
        world.Forge.Happened.Add(PollerWorld.LabeledBy("alice", 7002));
        var body = System.Text.Encoding.UTF8.GetBytes("{}");
        var signed = new Dictionary<string, string> { ["X-Event-Signature"] = HookVerification.Sign(world.Forge.Signature, "s3cret", body) };

        var verdicts = new[]
        {
            await world.Nudges.VerifyAsync(PollerWorld.Rule, signed, body, "s3cret", TestContext.Current.CancellationToken),
            await world.Nudges.VerifyAsync(PollerWorld.Rule with { Connection = "plain" }, signed, body, "s3cret", TestContext.Current.CancellationToken),
            await world.Nudges.VerifyAsync(PollerWorld.Rule with { Connection = "gone" }, signed, body, "s3cret", TestContext.Current.CancellationToken),
        };
        await world.Poller.HandleAsync(new ForgeTriggerNudged(PollerWorld.Labeled, Guid.NewGuid()), TestContext.Current.CancellationToken);
        await world.SettledAsync();

        Assert.Equal([DeliveryVerdict.Accepted, DeliveryVerdict.BadSignature, DeliveryVerdict.BadSignature], verdicts);
        Assert.Single(world.Triggers.Fired);
        Assert.Equal(world.Clock.GetUtcNow(), Outcomes.Present(Outcomes.Present(world.Book.WatchOf(PollerWorld.Labeled)).LastPolled));
    }

    [Fact]
    public async Task AnAcknowledgingTriggerCommentsOnTheIssueWhoseEventStartedAJobAsync()
    {
        await using var world = await new PollerWorld().StartedAsync();
        world.Triggers.Declared[0] = world.Triggers.Declared[0] with { Forge = PollerWorld.Rule with { Acknowledge = true } };
        _ = await world.CheckAsync();
        world.Forge.Happened.AddRange([PollerWorld.LabeledBy("mallory", 7002), PollerWorld.LabeledBy("alice", 7003)]);

        _ = await world.CheckAsync();

        Assert.Equal(["#12: Avala started a job for this: trigger labeled."], world.Forge.Comments);
    }
}
