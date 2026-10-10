using Avala.Sdk;
using Avala.Testing;
using Avala.Triggers.Contracts;

namespace Avala.Triggers.Tests.Scheduling;

public sealed class TriggerSchedulerTests
{
    private static readonly TriggerId Nightly = new(TriggerId.Machine, "nightly");

    [Fact]
    public async Task AFixedTimeFiresAtItsLocalTimeAndArmsTheNextOccurrenceAsync()
    {
        await using var triggered = await StartAsync(""" "schedule": { "at": "09:00" } """);
        var nine = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal(Option<DateTimeOffset>.Some(nine), triggered.Scheduler.NextOf(Nightly));
        triggered.Clock.Advance(nine - triggered.Clock.GetUtcNow());
        var run = await triggered.FiredAsync(_ => true);

        Assert.Equal((TriggerOrigin.Schedule, "schedule", RunOutcome.Submitted), (run.Origin, run.Who, run.Outcome));
        Assert.Equal(nine.AddDays(1), await NextAsync(triggered, nine));
    }

    [Fact]
    public async Task AnIntervalCountsFromWhenItWasFirstSeenNotFromItsLoadAsync()
    {
        var triggered = await StartAsync(""" "schedule": { "everyMinutes": 60 } """);
        var due = Triggered.Monday.AddHours(1);

        await using var restarted = await triggered.RestartAsync(TimeSpan.FromMinutes(30));

        Assert.Equal(Option<DateTimeOffset>.Some(due), restarted.Scheduler.NextOf(Nightly));
        Assert.Empty(restarted.Triggers.RunsOf(Nightly));
    }

    [Fact]
    public async Task RunsMissedWhileClosedAreCaughtUpExactlyOnceAsync()
    {
        var triggered = await StartAsync(""" "schedule": { "everyMinutes": 60 }, "catchUp": "once" """);

        await using var restarted = await triggered.RestartAsync(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(10));

        var run = Assert.Single(restarted.Triggers.RunsOf(Nightly));
        Assert.Equal((TriggerOrigin.CatchUp, RunOutcome.Submitted), (run.Origin, run.Outcome));
        Assert.Equal(Option<DateTimeOffset>.Some(Triggered.Monday.AddHours(4)), restarted.Scheduler.NextOf(Nightly));
        Assert.Single(restarted.Jobs.Requests);
    }

    [Fact]
    public async Task RunsMissedWhileClosedWithoutCatchUpAreRecordedAsMissedAndNothingFiresAsync()
    {
        var triggered = await StartAsync(""" "schedule": { "everyMinutes": 60 } """);

        await using var restarted = await triggered.RestartAsync(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(10));

        var run = Assert.Single(restarted.Triggers.RunsOf(Nightly));
        Assert.Equal((RunOutcome.Missed, 3), (run.Outcome, run.Missed));
        Assert.Empty(restarted.Jobs.Requests);
        Assert.Equal(Option<DateTimeOffset>.Some(Triggered.Monday.AddHours(4)), restarted.Scheduler.NextOf(Nightly));
    }

    [Fact]
    public async Task ATimerThatRingsLatePastSeveralOccurrencesFollowsCatchUpInsteadOfABurstAsync()
    {
        await using var triggered = await StartAsync(""" "schedule": { "everyMinutes": 60 }, "catchUp": "once" """);

        triggered.Clock.Advance(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(10));
        var run = await triggered.FiredAsync(_ => true);
        _ = await NextAsync(triggered, Triggered.Monday.AddHours(3));

        Assert.Equal(TriggerOrigin.CatchUp, run.Origin);
        Assert.Single(triggered.Jobs.Requests);
    }

    [Fact]
    public async Task ADisabledTriggerArmsNoTimerAndRunNowStillFiresItAsync()
    {
        await using var triggered = await StartAsync(""" "schedule": { "everyMinutes": 60 } """);

        Outcomes.Succeeds(await triggered.Triggers.EnableAsync(Nightly, enabled: false, Triggered.Cancellation));
        var state = Assert.Single(await triggered.Triggers.ListAsync(Triggered.Cancellation));
        var run = Outcomes.Succeeds(await triggered.Triggers.FireAsync(Nightly, new FireRequest(TriggerOrigin.Manual, "person"), Triggered.Cancellation));

        Assert.Equal((false, Option<DateTimeOffset>.None), (state.Enabled, state.NextRun));
        Assert.Equal(RunOutcome.Submitted, run.Outcome);
        Assert.Equal(TriggerError.UnknownTrigger, Outcomes.FailsWith(await triggered.Triggers.EnableAsync(new TriggerId(TriggerId.Machine, "gone"), enabled: true, Triggered.Cancellation)));
    }

    [Fact]
    public async Task DisablingSurvivesARestartAndEnablingArmsAgainAsync()
    {
        var triggered = await StartAsync(""" "schedule": { "everyMinutes": 60 } """);
        _ = await triggered.Triggers.EnableAsync(Nightly, enabled: false, Triggered.Cancellation);

        await using var restarted = await triggered.RestartAsync(TimeSpan.FromHours(2));
        var disabled = restarted.Scheduler.NextOf(Nightly);
        _ = await restarted.Triggers.EnableAsync(Nightly, enabled: true, Triggered.Cancellation);

        Assert.Equal(Option<DateTimeOffset>.None, disabled);
        Assert.True(restarted.Scheduler.NextOf(Nightly).Match(next => next > restarted.Clock.GetUtcNow(), () => false));
    }

    [Fact]
    public async Task ATriggerRemovedFromItsFileIsNotFiredWhenItsTimeComesAsync()
    {
        await using var triggered = await StartAsync(""" "schedule": { "everyMinutes": 60 } """);
        triggered.World.Files.Machine = "{}";

        triggered.Clock.Advance(TimeSpan.FromHours(1));
        _ = await triggered.Bus.WaitForAsync<TriggersChanged>(changed => changed.At >= Triggered.Monday.AddHours(1), Triggered.Cancellation);

        Assert.Empty(triggered.Jobs.Requests);
        Assert.Equal(Option<DateTimeOffset>.None, triggered.Scheduler.NextOf(Nightly));
    }

    private static Task<Triggered> StartAsync(string fields) =>
        Triggered.StartAsync(Declared.Machine(Declared.Trigger(fields)));

    private static async Task<DateTimeOffset> NextAsync(Triggered triggered, DateTimeOffset after)
    {
        _ = await triggered.Bus.WaitForAsync<TriggersChanged>(changed => changed.At >= after, Triggered.Cancellation);

        return triggered.Scheduler.NextOf(Nightly).Match(next => next, () => DateTimeOffset.MinValue);
    }
}
