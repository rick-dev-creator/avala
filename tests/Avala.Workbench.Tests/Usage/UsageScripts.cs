using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Spending;
using Avala.Workbench.Timeline;
using Avala.Workbench.Usage;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Usage;

public sealed class UsageViewModelScripts : IDisposable
{
    private readonly SessionBook sessions = new();
    private readonly FakeUsage usage = new();
    private readonly FakeBudgets budgets = new();
    private readonly FakeSupervision supervision = new();
    private readonly TestUiDispatcher ui = new();

    [Fact]
    public async Task TheUsagePageFollowsWhileActiveAsync()
    {
        var job = Pages.Summary("Fix JPY rounding in invoice totals", JobStatus.Running);
        usage.Connections.Add(new ConnectionUsage(new ConnectionName("claude-work"), Pages.Simulator, Pages.Used(1m)));
        usage.Jobs[job.Job] = Pages.Used(1m);
        using var page = Page(Pages.Board(job));

        page.Activate();

        await ui.PresentedAsync(page, () => page.Connections.Count == 1 && page.Range.Days.Count == 7 && page.Jobs.Count == 1, () => $"{page.Connections.Count} connections, {page.Jobs.Count} jobs");
        Assert.Equal(("Usage", "claude-work", "1 USD"), await ui.ReadAsync(() => (page.Title, page.Connections[0].Name, page.Jobs[0].Cost)));
    }

    [Fact]
    public async Task UsageRecordedLaterShowsOnTheNextBeatAsync()
    {
        var board = Pages.Board();
        using var page = Page(board);
        page.Activate();
        await ui.PresentedAsync(page, () => page.Range.Days.Count == 7, () => $"{page.Range.Days.Count} days");

        usage.Connections.Add(new ConnectionUsage(new ConnectionName("claude-personal"), Pages.Simulator, Pages.Used(0.84m)));
        board.Publish(board.Jobs);

        await ui.PresentedAsync(page, () => page.Connections.Count == 1, () => $"{page.Connections.Count} connections");
        Assert.Equal("claude-personal", await ui.ReadAsync(() => page.Connections[0].Name));
    }

    [Fact]
    public async Task WithNothingSpentThePageListsNoConnectionsNoJobsAndNoInterventionsAsync()
    {
        using var page = Page(Pages.Board(Pages.Summary("Update lodash to 4.17.21", JobStatus.Preparing)));

        page.Activate();

        await ui.PresentedAsync(page, () => page.Range.Days.Count == 7, () => $"{page.Range.Days.Count} days");
        Assert.Equal((0, 0, 0), await ui.ReadAsync(() => (page.Connections.Count, page.Jobs.Count, page.Interventions.Count)));
    }

    [Fact]
    public async Task ChoosingAWindowRereadsItsDaysAndItsTotalByTokenTypeAsync()
    {
        var history = new FakeUsageHistory();
        using var page = Page(Pages.Board(), history);
        page.Activate();
        await ui.PresentedAsync(page, () => page.Range.Days.Count == 7, () => $"{page.Range.Days.Count} days");

        await ui.InvokeAsync(() => page.Range.ShowMonthCommand.Execute(null), TestContext.Current.CancellationToken);
        await ui.PresentedAsync(page, () => page.Range.Days.Count == 30, () => $"{page.Range.Days.Count} days");
        var month = await ui.ReadAsync(() => (page.Range.ShowsMonth, page.Range.ShowsWeek, page.Range.Total!.Label, page.Range.Days[0].Day, page.Range.Caption));

        await ui.InvokeAsync(() => page.Range.ShowTodayCommand.Execute(null), TestContext.Current.CancellationToken);
        await ui.PresentedAsync(page, () => page.Range.Days.Count == 1, () => $"{page.Range.Days.Count} days");

        Assert.Equal((true, false, "Last 30 days", "Fri 9 Oct · today", "Since Thu 10 Sep, in this computer's time zone · newest first"), month);
        Assert.Equal(("Today", "Today, in this computer's time zone"), await ui.ReadAsync(() => (page.Range.Total!.Label, page.Range.Caption)));
        Assert.Equal([(new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 9)), (new DateOnly(2026, 9, 10), new DateOnly(2026, 10, 9)), (new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 9))], history.Daily);
    }

    public void Dispose() => ui.Dispose();

    private UsageViewModel Page(JobBoard board, FakeUsageHistory? history = null) =>
        new(
            new UsageReader(usage, new JobSpending(usage, budgets, supervision, sessions), board),
            new UsageWindows(history ?? new FakeUsageHistory(), Clock),
            new LiveFeed(new Pulse(board), ui));

    private static FakeTimeProvider Clock
    {
        get
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 9, 15, 0, 0, TimeSpan.Zero));
            clock.SetLocalTimeZone(TimeZoneInfo.Utc);

            return clock;
        }
    }
}

public sealed class UsageRangeViewModelScripts
{
    [Fact]
    public void TheDaysOfAWindowAreNewestFirstWithABarAgainstTheBusiestDay() =>
        ViewModelScript.Given(new UsageRangeViewModel())
            .When(range => range.Show(new UsageRange(
                UsageSpan.LastSevenDays,
                new DateOnly(2026, 10, 3),
                new DateOnly(2026, 10, 4),
                new UsagePeriod(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, Pages.Used(3m), [], []),
                [Day(new DateOnly(2026, 10, 3), 200), Day(new DateOnly(2026, 10, 4), 100)])))
            .ThenNotified(nameof(UsageRangeViewModel.Total), nameof(UsageRangeViewModel.Caption))
            .Then(range => Assert.Equal(
                [("Sun 4 Oct · today", 0.5, true), ("Sat 3 Oct", 1d, false)],
                range.Days.Select(day => (day.Day, day.Share, day.IsToday))))
            .Then(range => Assert.Equal(("Last 7 days", "3 USD"), (range.Total!.Label, range.Total.Cost)));

    [Fact]
    public void ChoosingTheShownWindowAgainAsksForNothing()
    {
        var range = new UsageRangeViewModel();
        var chosen = 0;
        range.Chosen += (_, _) => chosen++;

        range.ShowWeekCommand.Execute(null);
        range.ShowTodayCommand.Execute(null);

        Assert.Equal((1, true), (chosen, range.ShowsToday));
    }

    [Fact]
    public void ARangeReadForAnotherWindowIsNotShown() =>
        ViewModelScript.Given(new UsageRangeViewModel())
            .When(range => range.Show(new UsageRange(UsageSpan.Today, new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 4), Day(new DateOnly(2026, 10, 4), 1), [Day(new DateOnly(2026, 10, 4), 1)])))
            .Then(range => Assert.Equal((0, (IUsageWindowViewModel?)null), (range.Days.Count, range.Total)));

    private static UsagePeriod Day(DateOnly day, long input) =>
        new(new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), Pages.Used(1m) with { Tokens = new TokenUsage(input, 0, 0, 0, 0) }, [], []);
}

public sealed class UsageDayViewModelScripts
{
    [Fact]
    public void ADaySaysItsTokensByTypeAndItsCost() =>
        ViewModelScript.Given(new UsageDayViewModel(new UsagePeriod(new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.FromHours(2)), DateTimeOffset.UnixEpoch, Pages.Used(2m), [], []), 0, false))
            .Then(day => Assert.Equal(
                ("Fri 9 Oct", "159 tokens", "2 USD", "input 100 · output 20 · cache read 30 · cache write 4 · reasoning 5", 0d),
                (day.Day, day.Tokens, day.Cost, day.Types, day.Share)));
}

public sealed class LimitViewModelScripts
{
    [Fact]
    public void ALimitNearItsHoldThresholdSaysJobsAreHeldThere() =>
        ViewModelScript.Given(new LimitViewModel(new UsageLimit("5h", 0.88, Option<DateTimeOffset>.None), 0.9))
            .Then(limit => Assert.Equal(("5h", "5-hour window", 0.88, "88%", "no reset reported", "Jobs on this connection hold at the 90% threshold.", false), (limit.Window, limit.Label, limit.Used, limit.UsedText, limit.Resets, limit.HoldAt, limit.ReachesHold)))
            .Then(limit => Assert.Equal((true, 0.9, true), (limit.HasHold, limit.Hold, limit.IsNear)));

    [Fact]
    public void ALimitPastItsThresholdReachesTheHoldAndItsBarStopsAtFull() =>
        ViewModelScript.Given(new LimitViewModel(new UsageLimit("5h", 1.04, Option<DateTimeOffset>.None), 0.9))
            .Then(limit => Assert.Equal((true, true, 1d, "104%"), (limit.ReachesHold, limit.IsNear, limit.Used, limit.UsedText)));

    [Fact]
    public void ALimitFarFromItsThresholdIsNotNear() =>
        ViewModelScript.Given(new LimitViewModel(new UsageLimit("7d", 0.43, Option<DateTimeOffset>.None), 0.9))
            .Then(limit => Assert.Equal((false, false, "7-day window"), (limit.IsNear, limit.ReachesHold, limit.Label)));

    [Theory]
    [InlineData("5h", "5-hour window")]
    [InlineData("7d", "7-day window")]
    [InlineData("weekly", "Weekly window")]
    [InlineData("", "Usage window")]
    public void EachWindowIsNamedInWords(string window, string label) =>
        Assert.Equal(label, UsagePhrases.Window(window));

    [Fact]
    public void ALimitAtItsThresholdReachesTheHold() =>
        ViewModelScript.Given(new LimitViewModel(new UsageLimit("5h", 0.9, Option<DateTimeOffset>.None), 0.9))
            .Then(limit => Assert.True(limit.ReachesHold));

    [Fact]
    public void ALimitWithoutCapsHasNoThreshold() =>
        ViewModelScript.Given(new LimitViewModel(new UsageLimit("week", 0.31, DateTimeOffset.UnixEpoch), Option<double>.None))
            .Then(limit => Assert.Equal((string.Empty, false, false, true), (limit.HoldAt, limit.HasHold, limit.ReachesHold, limit.Resets.StartsWith("resets ", StringComparison.Ordinal))));
}

public sealed class ConnectionMeterViewModelScripts
{
    [Fact]
    public void AConnectionMeterShowsItsSpendAndTheCapsOfItsLatestSession() =>
        ViewModelScript.Given(new ConnectionMeterViewModel(new ConnectionSpend(
                new ConnectionUsage(new ConnectionName("claude-work"), Pages.Simulator, Pages.Used(3.214m, new UsageLimit("5h", 0.88, Option<DateTimeOffset>.None)) with { UnpricedReports = 1 }),
                new BudgetCaps([new Cost(5m, "USD")], Option<long>.None, 0.9))))
            .Then(meter =>
            {
                Assert.Equal(("claude-work", "Simulator", "3.214 USD", "159 tokens", "1 usage report had no cost"), (meter.Name, meter.Provider, meter.Cost, meter.Tokens, meter.Unpriced));
                Assert.Equal("5 USD per job, holds at 90% of a limit", meter.Caps);
                Assert.Equal("Jobs on this connection hold at the 90% threshold.", Assert.Single(meter.Limits).HoldAt);
            });

    [Fact]
    public void AConnectionWithoutAKnownSessionHasNoCapsYet() =>
        ViewModelScript.Given(new ConnectionMeterViewModel(new ConnectionSpend(new ConnectionUsage(new ConnectionName("claude-personal"), Pages.Simulator, Pages.Used(0m)), Option<BudgetCaps>.None)))
            .Then(meter => Assert.Equal("no caps known yet", meter.Caps));
}

public sealed class UsageWindowViewModelScripts
{
    [Fact]
    public void AWindowCountsItsTurnsAndTokensByType() =>
        ViewModelScript.Given(new UsageWindowViewModel("Last 7 days", new UsagePeriod(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, Pages.Used(7m), [], [])))
            .Then(window => Assert.Equal(("Last 7 days", "7 USD", 100L, 20L, 30L, 4L, 5L, "159 tokens", "1 turns finished, 0 interrupted, 0 failed"), (window.Label, window.Cost, window.Input, window.Output, window.CacheRead, window.CacheWrite, window.Reasoning, window.Tokens, window.Turns)));
}

public sealed class JobMeterViewModelScripts
{
    [Fact]
    public void AJobThatSpentWithoutCapsOrCarveSaysSo() =>
        ViewModelScript.Given(new JobMeterViewModel(new JobCost(
                new BoardJob(Pages.Summary("Rate-limit POST /login", JobStatus.AwaitingReview), Transcript.Empty),
                new JobSpend(Option<SessionSeen>.None, Pages.Used(0.84m), Option<BudgetCaps>.None, Option<BudgetCarve>.None),
                [])))
            .Then(meter => Assert.Equal(("Rate-limit POST /login", JobStatus.AwaitingReview, "0.84 USD", "159 tokens", "no caps known", string.Empty, 0), (meter.Title, meter.Status, meter.Cost, meter.Tokens, meter.Caps, meter.Carve, meter.Interventions)))
            .Then(meter => Assert.Equal((false, "no cap", 0d, false), (meter.HasCap, meter.Cap, meter.Used, meter.IsNearCap)));

    [Theory]
    [InlineData(1.5, 0.5, false)]
    [InlineData(2.7, 0.9, true)]
    [InlineData(3.6, 1, true)]
    public void AJobsCostIsMeasuredAgainstItsCapAndTurnsAmberNearIt(double spent, double used, bool near) =>
        ViewModelScript.Given(new JobMeterViewModel(new JobCost(
                new BoardJob(Pages.Summary("Extract sync queue into a module", JobStatus.Running, connection: "claude-work"), Transcript.Empty),
                new JobSpend(Option<SessionSeen>.None, Pages.Used((decimal)spent), new BudgetCaps([new Cost(3m, "USD")], Option<long>.None, Option<double>.None), Option<BudgetCarve>.None),
                [])))
            .Then(meter => Assert.Equal((true, "3 USD", "claude-work", near), (meter.HasCap, meter.Cap, meter.Connection, meter.IsNearCap)))
            .Then(meter => Assert.Equal(used, meter.Used, 3));
}

public sealed class InterventionViewModelScripts
{
    [Fact]
    public void ABudgetInterventionSaysWhatWasMeasuredAgainstWhichCap() =>
        ViewModelScript.Given(new InterventionViewModel(
                new JobIntervention(JobId.New(), DateTimeOffset.UnixEpoch, HoldReason.BudgetExceeded) { Breach = new BudgetBreach(BudgetMeasure.Cost, "USD", 5.25m, 5m, Option<BudgetError>.None) },
                "Extract sync queue into a module"))
            .Then(shown => Assert.Equal(("Extract sync queue into a module", HoldReason.BudgetExceeded, "Cost USD: 5.25 against 5"), (shown.Title, shown.Reason, shown.Detail)));

    [Fact]
    public void ASupervisorInterventionSaysHowLongTheAgentWasSilent() =>
        ViewModelScript.Given(new InterventionViewModel(
                new JobIntervention(JobId.New(), DateTimeOffset.UnixEpoch, HoldReason.Stalled) { Silence = new SilenceMeasure(TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(600)) },
                "Extract sync queue into a module"))
            .Then(shown => Assert.Equal("silent for 600s, window 600s", shown.Detail));

    [Fact]
    public void AnInterventionWithoutAMeasureHasNoDetail() =>
        ViewModelScript.Given(new InterventionViewModel(new JobIntervention(JobId.New(), DateTimeOffset.UnixEpoch, HoldReason.Stopped), "x"))
            .Then(shown => Assert.Empty(shown.Detail));
}
