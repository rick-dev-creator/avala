using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Testing;
using Avala.Workbench.Following;
using Avala.Workbench.Spending;
using Avala.Workbench.Usage;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Spending;

public sealed class SpendingTests : IDisposable
{
    private readonly SessionBook sessions;
    private readonly FakeUsage usage = new();
    private readonly FakeBudgets budgets = new();
    private readonly FakeSupervision supervision = new();
    private readonly TestUiDispatcher ui = new();

    public SpendingTests() => sessions = new SessionBook(usage);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void AfterARestartAJobAndAConnectionTakeTheirCapsAndAccountFromTheirLatestStoredSession()
    {
        var job = JobId.New();
        var account = new AgentAccount("team", "Team");
        var (older, latest) = (SessionId.New(), SessionId.New());
        usage.Stored.AddRange(
        [
            new UsageSession(older, Pages.Simulator, account, new ConnectionName("work"), DateTimeOffset.UnixEpoch) { Job = job },
            new UsageSession(latest, Pages.Simulator, account, new ConnectionName("work"), DateTimeOffset.UnixEpoch.AddHours(1)) { Job = job },
        ]);
        var caps = new BudgetCaps([new Cost(5m, "USD")], Option<long>.None, 0.8);
        budgets.Caps[latest] = caps;
        var spending = new JobSpending(usage, budgets, supervision, sessions);

        var spend = spending.Of(job);

        Assert.Equal((latest, Option<AgentAccount>.Some(account)), Outcomes.Present(spend.Session.Map(seen => (seen.Session, seen.Account))));
        Assert.Equal(Option<BudgetCaps>.Some(caps), spend.Caps);
        Assert.Equal(Option<BudgetCaps>.Some(caps), spending.CapsOn(new ConnectionName("work")));
        Assert.Equal(Option<BudgetCaps>.None, spending.CapsOn(new ConnectionName("personal")));
    }

    [Fact]
    public async Task AConnectionsLimitShowsItsResetAndTheThresholdItsLatestSessionHoldsJobsAtAsync()
    {
        var job = Pages.Summary("Fix the failing test", JobStatus.Running);
        var session = await sessions.OpenAsync("work", job.Job);
        budgets.Caps[session] = new BudgetCaps([], Option<long>.None, 0.8);
        usage.Connections.Add(new ConnectionUsage(new ConnectionName("work"), Pages.Simulator, Pages.Used(1m, new UsageLimit("5h", 0.85, DateTimeOffset.UnixEpoch))));

        var meter = new ConnectionMeterViewModel(Assert.Single(Reader(job).Read().Connections));

        var limit = Assert.Single(meter.Limits);
        Assert.Equal(("5h", "85%", "Jobs on this connection hold at the 80% threshold.", true), (limit.Window, limit.UsedText, limit.HoldAt, limit.ReachesHold));
        Assert.StartsWith("resets ", limit.Resets, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobsAreListedWithTheirCostAgainstTheirCapsAndCarveAndTheirInterventionsAcrossJobsLatestFirstAsync()
    {
        var spent = Pages.Summary("Fix the failing test", JobStatus.NeedsHelp);
        var stalled = Pages.Summary("Add an endpoint", JobStatus.NeedsHelp);
        var idle = Pages.Summary("Update a dependency", JobStatus.Running);
        var session = await sessions.OpenAsync("work", spent.Job);
        budgets.Caps[session] = new BudgetCaps([new Cost(5m, "USD")], 2_000_000, Option<double>.None);
        budgets.Carves[spent.Job] = new BudgetCarve(JobId.New(), spent.Job, [new Cost(2m, "USD")], Option<long>.None, 0.4, DateTimeOffset.UnixEpoch);
        usage.Jobs[spent.Job] = Pages.Used(5.25m) with { UnpricedReports = 2 };
        budgets.Interventions.Add(new BudgetIntervention(
            new JobHold(spent.Job, session, HoldReason.BudgetExceeded, SessionHalt.Interrupted),
            new BudgetBreach(BudgetMeasure.Cost, "USD", 5.25m, 5m, Option<BudgetError>.None),
            DateTimeOffset.UnixEpoch.AddMinutes(1)));
        supervision.Interventions.Add(new SupervisionIntervention(
            new JobHold(stalled.Job, SessionId.New(), HoldReason.Stalled, SessionHalt.Interrupted),
            new SilenceMeasure(TimeSpan.FromSeconds(95), TimeSpan.FromSeconds(90)),
            DateTimeOffset.UnixEpoch.AddMinutes(2)));

        var state = Reader(spent, stalled, idle).Read();

        var meters = state.Jobs.Select(cost => new JobMeterViewModel(cost)).ToList();
        Assert.Equal(
            [
                ("Fix the failing test", "5.25 USD", "5 USD per job, 2,000,000 tokens per job", "carved 2 USD", "2 usage reports had no cost", 1),
                ("Add an endpoint", "no cost", "no caps known", string.Empty, string.Empty, 1),
            ],
            meters.Select(meter => (meter.Title, meter.Cost, meter.Caps, meter.Carve, meter.Unpriced, meter.Interventions)).OrderBy(row => row.Item1 == "Add an endpoint"));
        Assert.Equal(
            [(HoldReason.Stalled, "silent for 95s, window 90s"), (HoldReason.BudgetExceeded, "Cost USD: 5.25 against 5")],
            state.Interventions.Select(intervention => new InterventionViewModel(intervention, "x")).Select(shown => (shown.Reason, shown.Detail)));
    }

    [Theory]
    [InlineData("Today", 10, 1)]
    [InlineData("LastSevenDays", 4, 7)]
    [InlineData("LastThirtyDays", -19, 30)]
    public async Task AWindowIsTheLastLocalCalendarDaysUpToNowReadAsAWholeAndDayByDayAsync(string span, int firstDay, int days)
    {
        var history = new FakeUsageHistory();
        var zone = TimeZoneInfo.CreateCustomTimeZone("Plus2", TimeSpan.FromHours(2), "Plus2", "Plus2");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 9, 23, 30, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(zone);
        var first = new DateOnly(2026, 10, 10).AddDays(firstDay - 10);

        var range = await new UsageWindows(history, clock).ReadAsync(Enum.Parse<UsageSpan>(span), Cancellation);

        Assert.Equal([(first, new DateOnly(2026, 10, 10))], history.Daily);
        Assert.Equal([(new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(2)), clock.GetUtcNow())], history.Within);
        Assert.Equal((first, new DateOnly(2026, 10, 10), days), (range.First, range.Today, range.Days.Count));
    }

    public void Dispose() => ui.Dispose();

    private UsageReader Reader(params JobSummary[] jobs) =>
        new(Pages.Readings(usage), new JobSpending(usage, budgets, supervision, sessions), Pages.Board(jobs));
}
