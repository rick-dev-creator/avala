using Avala.Agents.Contracts.Events;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Looping;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Tests.Looping;

public sealed class LoopHoldTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobHeldWithTheWindowItRanOutOfPausesTheLoopUntilThatWindowResetsBeforeAnyReadingOfItArrivesAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("one", "Greet the team");
        await pilot.StartAsync();
        var resets = pilot.Clock.GetUtcNow().AddHours(1);
        var job = await HeldAsync(pilot, Hold(default, HoldReason.LimitNearlyReached) with { Limit = new UsageLimit("5h", 1, resets) });

        var paused = await pilot.Bus.WaitForAsync<LoopPaused>(_ => true, Cancellation);
        pilot.Clock.Advance(TimeSpan.FromHours(1));
        await pilot.Bus.WaitForAsync<LoopResumed>(_ => true, Cancellation);

        Assert.Equal((Option<DateTimeOffset>.Some(resets), Option<string>.Some("5h")), (paused.Pause.Until, paused.Pause.Window));
        Assert.Equal([(job, LoopRunner.ResetMessage)], pilot.Jobs.Continued);
        Assert.Empty(pilot.Bus.Published.OfType<LoopIterated>());
    }

    [Fact]
    public async Task AJobHeldAtTheDailySpendCeilingPausesTheLoopUntilTheLocalDayTurnsAndThenContinuesItWithoutCountingAFailureAsync()
    {
        await using var pilot = new Pilot();
        pilot.Clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("plus-one", TimeSpan.FromHours(1), "plus one", "plus one"));
        pilot.Backlog.Add("one", "Greet the team");
        await pilot.StartAsync(new LoopLimits { FailuresInARow = 1 });
        var start = pilot.Clock.GetUtcNow();
        var job = await HeldAsync(pilot, Hold(default, HoldReason.BudgetExceeded));
        var hold = Hold(job, HoldReason.BudgetExceeded);

        await pilot.Feed.HandleAsync(new BudgetIntervened(new BudgetIntervention(hold, Breach(BudgetMeasure.DailySpend), start)), Cancellation);
        var paused = await pilot.Bus.WaitForAsync<LoopPaused>(_ => true, Cancellation);
        pilot.Clock.Advance(TimeSpan.FromHours(1));
        await pilot.Bus.WaitForAsync<LoopResumed>(_ => true, Cancellation);

        Assert.Equal(new LoopPause(PauseReason.DailySpend, start, start.AddHours(1)), paused.Pause);
        Assert.Equal([(job, LoopRunner.NewDayMessage)], pilot.Jobs.Continued);
        Assert.Empty(pilot.Bus.Published.OfType<LoopIterated>());
    }

    [Fact]
    public async Task AJobHeldOverItsOwnCostCapWaitsForAPersonAndCountsAsAFailureOfTheLoopAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("one", "Greet the team");
        pilot.Backlog.Add("two", "Document the greeting");
        await pilot.StartAsync(new LoopLimits { FailuresInARow = 1 });
        var job = await pilot.TakenAsync(1);
        pilot.Budgets.Intervened(new BudgetIntervention(Hold(job, HoldReason.BudgetExceeded), Breach(BudgetMeasure.Cost), pilot.Clock.GetUtcNow()));

        await HeldAsync(pilot, job, Hold(job, HoldReason.BudgetExceeded));
        var iteration = await pilot.IteratedAsync(1);
        var ended = await pilot.EndedAsync();

        Assert.Equal((IterationOutcome.NeedsHelp, Option<HoldReason>.Some(HoldReason.BudgetExceeded)), (iteration.Outcome, iteration.Hold));
        Assert.Equal(Option<Breaker>.Some(Breaker.FailuresInARow), ended.Breaker);
        Assert.Empty(pilot.Jobs.Continued);
    }

    private static JobHold Hold(JobId job, HoldReason reason) => new(job, default, reason, SessionHalt.Interrupted);

    private static BudgetBreach Breach(BudgetMeasure measure) => new(measure, "USD", 10.5m, 10m, Option<BudgetError>.None);

    private static async Task<JobId> HeldAsync(Pilot pilot, JobHold shape)
    {
        var job = await pilot.TakenAsync(1);
        await HeldAsync(pilot, job, shape with { Job = job });

        return job;
    }

    private static async Task HeldAsync(Pilot pilot, JobId job, JobHold hold)
    {
        pilot.Jobs.Attempts(job, Pilot.Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Interrupted));
        await pilot.ProgressAsync(job, JobStatus.NeedsHelp);
        await pilot.Feed.HandleAsync(new JobHeld(hold), Cancellation);
    }
}
